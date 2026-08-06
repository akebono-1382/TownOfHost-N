using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using TownOfHost.Roles.Neutral;
using UnityEngine;
using TownOfHost.Modules;

namespace TownOfHost.Roles.Impostor
{
    public sealed class Polaris : RoleBase, IImpostor, IUsePhantomButton
    {
        public static readonly SimpleRoleInfo RoleInfo =
            SimpleRoleInfo.Create(
                typeof(Polaris),
                player => new Polaris(player),
                CustomRoles.Polaris,
                () => RoleTypes.Phantom,
                CustomRoleTypes.Impostor,
                10000,
                SetUpOptionItem,
                "Pol",
                "#ff1919",
                OptionSort: (7, 0)
            );
        public Polaris(PlayerControl player)
        : base(
            RoleInfo,
            player
        )
        {
            KillCooldown = OptionKillCooldown.GetFloat();
            shine = OptionFirstshine.GetInt();
            DecreasingTime = OptionDecreasingTime.GetFloat();
            Reducedperkill = OptionReducedperkill.GetInt();

            DecreasingTimer = null;

            // 初期値
            phantomCooldownTimer = 0f;
            prevPhantomCooldownTimer = 0f;
            bombTriggered = false;
        }

        private static OptionItem OptionKillCooldown;
        private static float KillCooldown;
        public static OptionItem OptionFirstshine;
        public static OptionItem OptionDecreasingTime;
        private static float DecreasingTime;
        public int shine;
        public float? DecreasingTimer;
        public static OptionItem OptionReducedperkill;
        public int Reducedperkill;
        public static OptionItem OptionBombCooldown;
        public static OptionItem OptionExplosionRadius;


        enum OptionName
        {
            PolarisFirstshine,
            PolarisDecreasingTime,
            PolarisReducedperkill,
            PolarisBombCooldown,
            PolarisExplosionRadius,
        }


        public bool CanBeLastImpostor { get; } = false;

        // ホスト側で減らす想定。クライアント表示でも同様に使えます。
        private float phantomCooldownTimer;         // 残り秒数（>=0）
        private float prevPhantomCooldownTimer;     // 前フレームの残り秒数（エッジ検出用）
        private bool bombTriggered;                 // 爆発を一度だけにするフラグ

        /// <summary>
        /// アビリティの表示用クールダウンを設定する（副作用はない）。
        /// </summary>
        public void SetPhantomCooldown(float seconds)
        {
            phantomCooldownTimer = Mathf.Max(0f, seconds);
            prevPhantomCooldownTimer = phantomCooldownTimer;

            // ホストなら表示更新を通知（クライアントHUD反映）
            if (AmongUsClient.Instance.AmHost) UtilsNotifyRoles.NotifyRoles();
        }

        /// <summary>
        /// ファントムクールダウンが 0 に到達した瞬間に呼ばれる（表示更新のみ）。
        /// 実行直後に爆発条件を満たすなら爆発を行う。
        /// </summary>
        private void OnPhantomCooldownFinished()
        {
            UtilsNotifyRoles.NotifyRoles();

            // タイマー終了時に shine 条件を満たしていれば爆発を実行（ホストで呼ばれる想定）
            if (AmongUsClient.Instance.AmHost && !bombTriggered && shine <= 1)
            {
                // 爆発は所有者が生存している場合のみ行う（仕様どおり）
                if (Player.IsAlive())
                {
                    ExecuteExplosion();
                }
            }
        }

        /// <summary>
        /// ホストでのみ実行される爆発処理。ダメージは周囲プレイヤーに与え、
        /// 所有者が生存していれば自殺処理も行う。実行は一度だけ。
        /// </summary>
        private void ExecuteExplosion()
        {
            //string modFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            //string soundPath = Path.Combine(Directory.GetCurrentDirectory(), "PolarisBomb.wav");
            // 一度だけ実行する
            bombTriggered = true;

            var explosionRadius = OptionExplosionRadius.GetFloat();
            var targets = PlayerCatch.AllAlivePlayerControls.ToArray();

            foreach (var target in targets)
            {
                if (target.PlayerId == Player.PlayerId) continue;
                if (!Ballooner.IsInExplosionRange(Player, target, explosionRadius)) continue;

                // ホスト側で対象プレイヤーに対して殺害判定を行う
                CustomRoleManager.OnCheckMurder(Player, target, target, target, true, false, 2, CustomDeathReason.Bombed);
            }

            // 所有者が生存しているなら自殺（ホストで実行）
            if (Player.IsAlive())
            {
                MyState.DeathReason = CustomDeathReason.Burnout;
                Player.SetRealKiller(Player);
                Player.RpcMurderPlayer(Player);
            }

            // 勝利判定（全滅など）
            if (!PlayerCatch.AllAlivePlayerControls.Any())
            {
                CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Impostor, byte.MaxValue);
            }

            // 状態同期と表示更新
            SendRPC();
            UtilsNotifyRoles.NotifyRoles();
        }

        private static void SetUpOptionItem()
        {
            OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0f, 60f, 2.5f), 22.5f, false)
                .SetValueFormat(OptionFormat.Seconds);
            OptionFirstshine = IntegerOptionItem.Create(RoleInfo, 11, OptionName.PolarisFirstshine, new(2, 99, 1), 10, false);
            OptionDecreasingTime = FloatOptionItem.Create(RoleInfo, 12, OptionName.PolarisDecreasingTime, new(1f, 50f, 0.5f), 10f, false)
                .SetValueFormat(OptionFormat.Seconds);
            OptionReducedperkill = IntegerOptionItem.Create(RoleInfo, 14, OptionName.PolarisReducedperkill, new(0, 98, 1), 1, false);
            OptionBombCooldown = FloatOptionItem.Create(RoleInfo, 15, OptionName.PolarisBombCooldown, new(2.5f, 60f, 2.5f), 30f, false)
                .SetValueFormat(OptionFormat.Seconds);
            OptionExplosionRadius = FloatOptionItem.Create(RoleInfo, 1, OptionName.PolarisExplosionRadius, new(0.5f, 10f, 0.5f), 3f, false)
                .SetValueFormat(OptionFormat.Multiplier);

        }

        public override void Add()
        {
            base.Add();
        }

        public override void OnSpawn(bool initialState = false)
        {
            base.OnSpawn(initialState);

            // ゲーム開始時点でタイマーを起動（ホストのみ）
            if (AmongUsClient.Instance.AmHost)
            {
                bombTriggered = false;
            }
        }

        public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
        {
            DecreasingTimer = null;
        }

        public override void OnFixedUpdate(PlayerControl player)
        {

            if (AmongUsClient.Instance.AmHost && !ExileController.Instance)
            {
                SendRPC();
                if (DecreasingTimer == null) //タイマーがない
                {
                    SetPhantomCooldown(OptionBombCooldown.GetFloat());
                    DecreasingTimer = 0f;
                }
                else if (DecreasingTimer >= DecreasingTime)
                {
                    shine = --shine;
                    DecreasingTimer = 0f;
                    SendRPC();
                }
                else
                {
                    DecreasingTimer += Time.fixedDeltaTime;//時間をカウント
                }

                // --- ファントムクールダウンのカウントダウンとエッジ検出（ホストでのみ） ---
                prevPhantomCooldownTimer = phantomCooldownTimer;
                if (phantomCooldownTimer > 0f)
                {
                    phantomCooldownTimer -= Time.fixedDeltaTime;
                    if (phantomCooldownTimer < 0f) phantomCooldownTimer = 0f;
                }

                // エッジ検出：prev > 0 && now == 0
                if (prevPhantomCooldownTimer > 0f && phantomCooldownTimer <= 0f)
                {
                    OnPhantomCooldownFinished();
                }

                // 追加の保険：タイマーが既に0の状態でも条件を満たせば確実に発動する
                if (phantomCooldownTimer <= 0f && !bombTriggered && shine <= 1 && Player.IsAlive())
                {
                    ExecuteExplosion();
                }
            }
            if (shine <= 0)
            {
                MyState.DeathReason = CustomDeathReason.Burnout;
                Player.SetRealKiller(Player);
                Player.RpcMurderPlayer(Player);
            }
        }

        private void SendRPC()
        {
            using var sender = CreateSender();
            sender.Writer.Write(shine);
        }

        public override void ReceiveRPC(MessageReader reader)
        {
            shine = reader.ReadInt32();

            UtilsNotifyRoles.NotifyRoles();
        }

        public override string GetProgressText(bool comms = false, bool GameLog = false)
        {
            var color = RoleInfo?.RoleColorCode ?? "#ffffff";
            return $"<{color}>({shine})</color>";
        }

        public float CalculateKillCooldown() => KillCooldown;

        bool IUsePhantomButton.IsresetAfterKill => false;

        public override void ApplyGameOptions(IGameOptions opt)
        {
            AURoleOptions.PhantomCooldown = OptionBombCooldown.GetFloat();
        }

        // ファントムボタンは何もしない（クールダウン表示は最初から始まっている）
        void IUsePhantomButton.OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
        {
            AdjustKillCooldown = false;
            ResetCooldown = false;
            // 何もしない（仕様どおり）
        }

        public void OnCheckMurderAsKiller(MurderInfo info)
        {
            shine = shine - Reducedperkill;
            SendRPC();
        }

        public override void AfterMeetingTasks()
        {
            // ミーティング後は表示用クールの状態をクリア
            phantomCooldownTimer = 0f;
            prevPhantomCooldownTimer = 0f;
            bombTriggered = false;
        }
    }
}