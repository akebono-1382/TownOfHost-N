using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using LibCpp2IL;
using TownOfHost.Modules;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using TownOfHost.Roles.Crewmate;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

namespace TownOfHost.Roles.Neutral
{
    public sealed class Pelican : RoleBase, ILNKiller
    {
        public static readonly SimpleRoleInfo RoleInfo =
            SimpleRoleInfo.Create(
                typeof(Pelican),
                player => new Pelican(player),
                CustomRoles.Pelican,
                () => RoleTypes.Impostor,
                CustomRoleTypes.Neutral,
                552700,
                SetupOptionItem,
                "pel",
                "#75cc88",
                (2, 2),
                true,
                countType: CountTypes.Pelican,
                assignInfo: new RoleAssignInfo(CustomRoles.Pelican, CustomRoleTypes.Neutral)
                {
                    AssignCountRule = new(1, 1, 1)
                },
               from: From.GooseGooseDuck
            );
        public Pelican(PlayerControl player)
        : base(
            RoleInfo,
            player,
            () => HasTask.False
        )
        {
            KillCooldown = OptionKillCooldown.GetFloat();
            CanVent = OptionCanVent.GetBool();
        }
        Dictionary<byte, (float timer, Vector2 originalPos)> GrimPlayers = new(14);
        public static OptionItem OptionKillCooldown;
        private static OptionItem OptionCooldown;
        public static OptionItem OptionCanVent;
        static OptionItem OptionHasImpostorVision;
        private static float KillCooldown;
        public static bool CanVent;
        public static bool CanUseSabotage;
        int aliveCount;
        int grimCount;
        private static void SetupOptionItem()
        {
            SoloWinOption.Create(RoleInfo, 9, defo: 1);
            OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0f, 180f, 0.5f), 40f, false)
                .SetValueFormat(OptionFormat.Seconds);
            OptionCanVent = BooleanOptionItem.Create(RoleInfo, 11, GeneralOption.CanVent, true, false);
            OptionHasImpostorVision = BooleanOptionItem.Create(RoleInfo, 12, GeneralOption.ImpostorVision, true, false);
            RoleAddAddons.Create(RoleInfo, 15, NeutralKiller: true);
        }  
        public float CalculateKillCooldown() => KillCooldown;
        public bool CanUseSabotageButton() => false;
        public bool CanUseImpostorVentButton() => CanVent;
        public override void ApplyGameOptions(IGameOptions opt)
        {
            opt.SetVision(OptionHasImpostorVision.GetBool());
        }
        public void OnCheckMurderAsKiller(MurderInfo info)
        {
            info.DoKill = false;
            (var killer, var target) = info.AttemptTuple;

            // 修正：ターゲットの現在の位置（元の位置）を取得して保持する
            var originalPosition = (Vector2)Player.transform.position;
            var hidePosition = new Vector2(-100f, -100f);
            target.RpcSnapToForced(hidePosition);

            if (!GrimPlayers.ContainsKey(target.PlayerId))
            {
                killer.SetKillCooldown(delay: true);
                // 修正：タイマー(999f)と元の座標を一緒に追加
                GrimPlayers.Add(target.PlayerId, (999f, originalPosition));
                RpcAddList(target.PlayerId, originalPosition); // RPC引数も拡張
            }
            UtilsNotifyRoles.NotifyRoles(SpecifySeer: [Player]);
            return;
        }

        // 目的の処理：ペリカンが死んだ（襲われた）時に全員を元の位置に戻す
        public override bool OnCheckMurderAsTarget(MurderInfo info)
        {
            foreach (var (targetId, data) in GrimPlayers)
            {
                var target = PlayerCatch.GetPlayerById(targetId);
                if (target != null)
                {
                    var originalPosition = (Vector2)Player.transform.position;
                    // 保持していた元の位置にプレイヤーを強制移動させる
                    target.RpcSnapToForced(data.originalPos);
                }
            }
            GrimPlayers.Clear(); // お腹の中をクリア

            return true; // 通常通り自分がキルされる処理を続行
        }

        public override void OnReportDeadBody(PlayerControl repo, NetworkedPlayerInfo __)
        {
            if (AddOns.Common.Amnesia.CheckAbilityreturn(Player)) return;
            foreach (var targetId in GrimPlayers.Keys)
            {
                var target = PlayerCatch.GetPlayerById(targetId);
                KillBitten(target, true);
            }
            GrimPlayers.Clear();
        }

        public override void OnFixedUpdate(PlayerControl player)
        {
            if (!GameStates.IsInTask || GameStates.CalledMeeting) return;

            List<byte> del = new();
            foreach (var (targetId, data) in GrimPlayers)
            {
                if (data.timer < 0)
                {
                    del.Add(targetId);
                    Main.AllPlayerKillCooldown[Player.PlayerId] = KillCooldown;
                    Player.SetKillCooldown(KillCooldown * 0.5f);
                    UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
                }
                else
                {
                    // 修正：Dictionaryの構造変更に伴うタイマーの減算
                    GrimPlayers[targetId] = (data.timer - Time.fixedDeltaTime, data.originalPos);
                }
            }
            aliveCount = 0;
            foreach (var p in PlayerControl.AllPlayerControls)
            {
                if (p.IsAlive())
                {
                    aliveCount++;
                }
            }

            grimCount = GrimPlayers.Count;

            // 比較を行う
            if (grimCount >= (aliveCount - 1))
            {
                foreach (var targetId in GrimPlayers.Keys)
                {
                    var target = PlayerCatch.GetPlayerById(targetId);
                    KillBitten(target, true);
                }
            }
        }
        private void KillBitten(PlayerControl target, bool isButton = false)
        {
            if (target == null) return;
            var Grim = Player;
            if (target.IsAlive() && Player.IsAlive())
            {
                if (!isButton && Grim.IsAlive()) RPC.PlaySoundRPC(Grim.PlayerId, Sounds.KillSound);
                PlayerState.GetByPlayerId(target.PlayerId).DeathReason = CustomDeathReason.Swallowing;
                target.RpcExileV3();
                PlayerState.GetByPlayerId(target.PlayerId).SetDead();
            }
        }
        public void RpcAddList(byte targetId, Vector2 originalPos)
        {
            using var sender = CreateSender();
            sender.Writer.Write(true);
            sender.Writer.Write(targetId);
            sender.Writer.Write(originalPos.x); // X座標
            sender.Writer.Write(originalPos.y); // Y座標
        }
        public bool OverrideKillButtonText(out string text)
        {
            text = GetString("Swallowing");
            return true;
        }

        public bool OverrideKillButton(out string text)
        {
            text = "Pel_Kill";
            return true;
        }
        public override void ReceiveRPC(MessageReader reader)
        {
            var targetId = reader.ReadByte();
            var x = reader.ReadSingle();
            var y = reader.ReadSingle();
            var originalPos = new Vector2(x, y);

            var result = GrimPlayers.TryAdd(targetId, (999f, originalPos));
            if (!result)
            {
                Logger.Warn($"既に{targetId}はGrimPlayersに含まれていたため、追加に失敗しました", "GrimReaper");
            }
        }
        public override void OnStartMeeting() => GrimPlayers.Clear();//ホスト以外はこっちでリセット
    }
}
