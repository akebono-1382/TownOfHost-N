using System.Collections.Generic;
using System.Linq;

using Hazel;
using UnityEngine;

using AmongUs.GameOptions;

using TownOfHost.Modules;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using TownOfHost.Roles.Crewmate;
using static TownOfHost.Roles.Core.Interfaces.ISchrodingerCatOwner;

namespace TownOfHost.Roles.Neutral;

// マッドが属性化したらマッド状態時の特別扱いを削除する
public sealed class SchrodingerCat : RoleBase, IAdditionalWinner, IDeathReasonSeeable, IKillFlashSeeable, IKiller, ISchrodingerCatOwner
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(SchrodingerCat),
            player => new SchrodingerCat(player),
            CustomRoles.SchrodingerCat,
            () => OptionHasKillButton.GetBool() ? RoleTypes.Impostor : RoleTypes.Crewmate,
            CustomRoleTypes.Neutral,
            54100,
            SetupOptionItem,
            "sc",
            "#696969",
            (7, 2),
            countType: CountTypes.Crew,
            introSound: () => GetIntroSound(RoleTypes.Impostor),
            from: From.TOR_GM_Haoming_Edition
        );
    public SchrodingerCat(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        CanWinTheCrewmateBeforeChange = OptionCanWinTheCrewmateBeforeChange.GetBool();
        ChangeTeamWhenExile = OptionChangeTeamWhenExile.GetBool();
        CanSeeKillableTeammate = OptionCanSeeKillableTeammate.GetBool();
        HasKillbutton = OptionHasKillButton.GetBool();
    }
    static OptionItem OptionCanWinTheCrewmateBeforeChange;
    static OptionItem OptionChangeTeamWhenExile;
    public bool CanKill;
    private static OptionItem OptionKillCooldown;
    public static OptionItem OptionCanVent;
    public static OptionItem OptionCanUseSabotage;
    public static OptionItem OptionHasImpostorVision;
    public static OptionItem OptionDieKiller;
    public static OptionItem OptionDieKillerTIme;
    static OptionItem OptionShowRoleNameToKiller;
    static OptionItem OptionShowRoleNameToKillerTeam;
    static OptionItem OptionCountChenge;
    static OptionItem OptionCanSeeKillableTeammate;
    static OptionItem OptionHasKillButton;
    PlayerControl Killer;
    byte KillerId = byte.MaxValue;
    readonly HashSet<byte> RoleNameSeerIds = [];
    static bool KillerisCat;
    public TeamType SchrodingerCatChangeTo => Team;
    enum OptionName
    {
        OpportunistHasKillButton,
        CanBeforeSchrodingerCatWinTheCrewmate,
        SchrodingerCatExiledTeamChanges,
        SchrodingerCatCanSeeKillableTeammate,
        BakeCatDieKiller,
        BakeCatDieKillerTime,
        BakeCatShowRoleNameToKiller,
        BakeCatShowRoleNameToKillerTeam,
        BakeCatCountChenge
    }
    static bool CanWinTheCrewmateBeforeChange;
    static bool ChangeTeamWhenExile;
    static bool CanSeeKillableTeammate;
    static bool HasKillbutton;

    /// <summary>
    /// 自分をキルしてきた人のロール
    /// </summary>
    private ISchrodingerCatOwner owner = null;
    private TeamType _team = TeamType.None;
    /// <summary>
    /// 現在の所属陣営<br/>
    /// 変更する際は特段の事情がない限り<see cref="RpcSetTeam"/>を使ってください
    /// </summary>
    public TeamType Team
    {
        get => _team;
        private set
        {
            logger.Info($"{Player.GetRealName()}の陣営を{value}に変更");
            _team = value;
        }
    }
    public bool AmMadmate => Team == TeamType.Mad;
    public Color DisplayRoleColor => GetCatColor(Team);
    private static LogHandler logger = Logger.Handler(nameof(SchrodingerCat));

    public static void SetupOptionItem()
    {
        OptionHasKillButton = BooleanOptionItem.Create(RoleInfo, 10, OptionName.OpportunistHasKillButton, false, false);
        OptionCanVent = BooleanOptionItem.Create(RoleInfo, 11, GeneralOption.CanVent, true, false, OptionHasKillButton);
        OptionCanUseSabotage = BooleanOptionItem.Create(RoleInfo, 12, GeneralOption.CanUseSabotage, false, false, OptionHasKillButton);
        OptionHasImpostorVision = BooleanOptionItem.Create(RoleInfo, 13, GeneralOption.ImpostorVision, true, false, OptionHasKillButton);
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 14, GeneralOption.KillCooldown, new(0f, 180f, 0.5f), 30f, false, OptionHasKillButton)
            .SetValueFormat(OptionFormat.Seconds);
        OptionCountChenge = BooleanOptionItem.Create(RoleInfo, 15, OptionName.BakeCatCountChenge, false, false, OptionHasKillButton);
        OptionShowRoleNameToKiller = BooleanOptionItem.Create(RoleInfo, 16, OptionName.BakeCatShowRoleNameToKiller, true, false, OptionHasKillButton);
        OptionShowRoleNameToKillerTeam = BooleanOptionItem.Create(RoleInfo, 17, OptionName.BakeCatShowRoleNameToKillerTeam, false, false, OptionShowRoleNameToKiller);
        OptionDieKiller = BooleanOptionItem.Create(RoleInfo, 18, OptionName.BakeCatDieKiller, true, false);
        OptionDieKillerTIme = FloatOptionItem.Create(RoleInfo, 19, OptionName.BakeCatDieKillerTime, new(0, 180, 1), 1, false, OptionDieKiller).SetValueFormat(OptionFormat.Seconds);
        OptionCanWinTheCrewmateBeforeChange = BooleanOptionItem.Create(RoleInfo, 20, OptionName.CanBeforeSchrodingerCatWinTheCrewmate, false, false);
        OptionChangeTeamWhenExile = BooleanOptionItem.Create(RoleInfo, 21, OptionName.SchrodingerCatExiledTeamChanges, false, false);
        OptionCanSeeKillableTeammate = BooleanOptionItem.Create(RoleInfo, 22, OptionName.SchrodingerCatCanSeeKillableTeammate, false, false);
    }
    public override void ApplyGameOptions(IGameOptions opt)
    {
        owner?.ApplySchrodingerCatOptions(opt);
    }
    /// <summary>
    /// マッド猫用のオプション構築
    /// </summary>
    public static void ApplyMadCatOptions(IGameOptions opt)
    {
        opt.SetVision(true);
    }
    void IKiller.OnCheckMurderAsKiller(MurderInfo info)
    {
        if (info.AttemptKiller.PlayerId == Player.PlayerId) return;

        // 親分はキル出来ないようにする
        if (info.AttemptTarget.PlayerId == (Killer?.PlayerId ?? byte.MaxValue) && !KillerisCat)
        {
            info.DoKill = false;
        }
    }
    public override bool OnCheckMurderAsTarget(MurderInfo info)
    {
        var killer = info.AttemptKiller;

        //自殺ならスルー
        if (info.IsSuicide) return true;
        if (!MagicalGirl.TryGetEffectiveRole<ISchrodingerCatOwner>(killer, out _)) return true;

        if (killer.Is(CustomRoles.GrimReaper) || killer.Is(CustomRoles.BakeCat))
        {
            return true;
        }
        else
            if (Team == TeamType.None)
        {
            info.CanKill = false;
            ChangeTeamOnKill(killer);
            if (Killer.Is(CustomRoles.SchrodingerCat) || Killer.Is(CustomRoles.BakeCat))
            {
                KillerisCat = true;
            }
            else
            {
                KillerisCat = false;
            }
            return false;
        }
        if (info.AttemptKiller.PlayerId == (Killer?.PlayerId ?? byte.MaxValue) && !KillerisCat)
        {
            return false;
        }
        return true;
    }
    /// <summary>
    /// キルしてきた人に応じて陣営の状態を変える
    /// </summary>
    public void ChangeTeamOnKill(PlayerControl killer)
    {
        if (!HasKillbutton)
        {
            killer.RpcProtectedMurderPlayer(Player);
            if (MagicalGirl.TryGetEffectiveRole<ISchrodingerCatOwner>(killer, out var catOwner))
            {
                catOwner.OnSchrodingerCatKill(this);
                RpcSetTeam(catOwner.SchrodingerCatChangeTo);
                owner = catOwner;
            }
            else
            {
                logger.Warn($"未知のキル役職からのキル: {killer.GetNameWithRole().RemoveHtmlTags()}");
            }

            RevealNameColors(killer);

            UtilsNotifyRoles.NotifyRoles();
            UtilsOption.MarkEveryoneDirtySettings();
        }
        else
        {
            killer.RpcProtectedMurderPlayer(Player);
            Killer = killer;
            if (MagicalGirl.TryGetEffectiveRole<ISchrodingerCatOwner>(killer, out var catOwner))
            {
                catOwner.OnSchrodingerCatKill(this);
                var newTeam = (TeamType)catOwner.SchrodingerCatChangeTo;
                SetRoleNameSeers(killer, newTeam);
                RpcSetTeam(newTeam);
                owner = catOwner;

                if (AmongUsClient.Instance.AmHost)
                {
                    Player.RpcSetRoleDesync(RoleTypes.Impostor, Player.GetClientId());
                    foreach (var pc in PlayerCatch.AllPlayerControls)
                    {
                        if (pc == PlayerControl.LocalPlayer)
                        {
                            Player.RpcSetRoleDesync(Player.IsAlive() ? RoleTypes.Crewmate : RoleTypes.CrewmateGhost, Player.GetClientId());
                            if (Player != pc) pc.RpcSetRoleDesync(pc.IsAlive() ? RoleTypes.Scientist : RoleTypes.CrewmateGhost, Player.GetClientId());
                        }
                        else
                        {
                            Player.RpcSetRoleDesync(pc == Player ? (Player.IsAlive() ? RoleTypes.Impostor : RoleTypes.ImpostorGhost) : (Player.IsAlive() ? RoleTypes.Crewmate : RoleTypes.CrewmateGhost), pc.GetClientId());
                            if (Player != pc) pc.RpcSetRoleDesync(pc.IsAlive() ? RoleTypes.Scientist : RoleTypes.CrewmateGhost, Player.GetClientId());
                        }
                    }
                }
                _ = new LateTask(() =>
                {
                    Player.SetKillCooldown(OptionKillCooldown.GetFloat(), force: true);
                    CanKill = true;
                    if (!Utils.RoleSendList.Contains(Player.PlayerId)) Utils.RoleSendList.Add(Player.PlayerId);

                    if (OptionCountChenge.GetBool())
                    {
                        MyState.SetCountType(killer.GetCustomRole().GetRoleInfo()?.CountType ?? CountTypes.Crew);
                        if (OptionDieKiller.GetBool())//死ぬならカウントが増えないようにキラーのカウントをクルーにしてやる
                            PlayerState.GetByPlayerId(killer.PlayerId).SetCountType(CountTypes.Crew);
                    }
                }, 0.3f, "ResetKillCooldown");
                if (OptionDieKiller.GetBool())
                    _ = new LateTask(() =>
                    {
                        if (!killer.IsAlive() || GameStates.CalledMeeting) return;
                        killer.RpcMurderPlayerV2(killer);
                    }, OptionDieKillerTIme.GetFloat(), "BakeCatKillerDie");
            }
            else
            {
                logger.Warn($"未知のキル役職からのキル: {killer.GetNameWithRole().RemoveHtmlTags()}");
                return;
            }

            RevealNameColors(killer);

            UtilsNotifyRoles.NotifyRoles(ForceLoop: true);
            UtilsOption.MarkEveryoneDirtySettings();

            if (PlayerControl.LocalPlayer.PlayerId == Player.PlayerId)
            {
                PlayerControl.LocalPlayer.Data.Role.AffectedByLightAffectors = false;
            }
        }
    }
    public override void OnReportDeadBody(PlayerControl repo, NetworkedPlayerInfo sitai)
    {
        if (OptionDieKiller.GetBool() && HasKillbutton)
        {
            if (Killer?.IsAlive() is not true) return;
            Killer.RpcMurderPlayerV2(Killer);
        }
    }
    private void SetRoleNameSeers(PlayerControl killer, TeamType team)
    {
        if (!HasKillbutton) return;
        RoleNameSeerIds.Clear();
        KillerId = killer?.PlayerId ?? byte.MaxValue;

        if (!OptionShowRoleNameToKiller.GetBool() || killer == null)
        {
            return;
        }

        RoleNameSeerIds.Add(killer.PlayerId);

        if (!OptionShowRoleNameToKillerTeam.GetBool())
        {
            return;
        }

        foreach (var member in PlayerCatch.AllPlayerControls.Where(member => IsRoleNameRevealTeamMember(member, team)))
        {
            RoleNameSeerIds.Add(member.PlayerId);
        }
    }
    private static bool IsRoleNameRevealTeamMember(PlayerControl player, TeamType team)
    {
        if (player == null || player.Data?.Disconnected == true)
        {
            return false;
        }

        return team switch
        {
            TeamType.Mad => player.Is(CustomRoleTypes.Impostor) || player.Is(CustomRoleTypes.Madmate) || player.Is(CustomRoles.WolfBoy),
            TeamType.Crew => player.Is(CountTypes.Crew),
            TeamType.Jackal => player.Is(CountTypes.Jackal) || player.Is(CustomRoles.Jackaldoll),
            TeamType.Egoist => player.Is(CustomRoles.Egoist),
            TeamType.CountKiller => player.Is(CustomRoles.CountKiller),
            TeamType.Remotekiller => player.Is(CountTypes.Remotekiller),
            TeamType.DoppelGanger => player.Is(CustomRoles.DoppelGanger),
            TeamType.MilkyWay => player.Is(CountTypes.MilkyWay),
            TeamType.Betrayer => player.Is(CustomRoles.MadBetrayer),
            TeamType.Pavlov => player.Is(CountTypes.Pavlov),
            TeamType.Opportunist => player.Is(CustomRoles.Opportunist),
            _ => false,
        };
    }
    private bool CanSeeRoleName(PlayerControl seer)
    {
        return Team != TeamType.None
            && OptionShowRoleNameToKiller.GetBool()
            && seer != null
            && RoleNameSeerIds.Contains(seer.PlayerId);
    }
    public override void OverrideDisplayRoleNameAsSeen(PlayerControl seer, ref bool enabled, ref Color roleColor, ref string roleText, ref bool addon)
    {
        if (CanSeeRoleName(seer))
        {
            enabled = true;
            roleColor = DisplayRoleColor;
            roleText = GetString(nameof(CustomRoles.SchrodingerCat));
            addon = false;
            return;
        }

        if (seer.IsAlive() is false && Team == TeamType.None)
        {
            roleText += $"{UtilsRoleText.GetRoleColorAndtext(CustomRoles.BakeCat)}";
        }
    }
    /// <summary>
    /// キルしてきた人とオプションに応じて名前の色を開示する
    /// </summary>
    private void RevealNameColors(PlayerControl killer)
    {
        if (CanSeeKillableTeammate)
        {
            var killerRoleId = killer.GetCustomRole();
            var killerTeam = PlayerCatch.AllPlayerControls.Where(player => (AmMadmate && (player.Is(CustomRoleTypes.Impostor) || player.Is(CustomRoles.WolfBoy))) || player.Is(killerRoleId));
            foreach (var member in killerTeam)
            {
                if (member.GetCustomRole().IsMadmate()) continue;
                var rolecolor = RoleInfo.RoleColorCode;
                if (member.Is(CustomRoles.WolfBoy))
                {
                    if (killerRoleId is not CustomRoles.WolfBoy) continue;
                    rolecolor = WolfBoy.Shurenekodotti.GetBool() ? UtilsRoleText.GetRoleColorCode(CustomRoles.Impostor) : "#ffffff";
                }
                NameColorManager.Add(member.PlayerId, Player.PlayerId, rolecolor);
                NameColorManager.Add(Player.PlayerId, member.PlayerId);
            }
        }
        else
        {
            var rolecolor = RoleInfo.RoleColorCode;
            if (killer.Is(CustomRoles.WolfBoy))
            {
                rolecolor = WolfBoy.Shurenekodotti.GetBool() ? UtilsRoleText.GetRoleColorCode(CustomRoles.Impostor) : "#ffffff";
            }
            NameColorManager.Add(killer.PlayerId, Player.PlayerId, rolecolor);
            NameColorManager.Add(Player.PlayerId, killer.PlayerId);
        }
        UtilsGameLog.AddGameLog($"SchrodingerCat", UtilsName.GetPlayerColor(Player) + ":  " + string.Format(GetString("SchrodingerCat.Ch"), UtilsName.GetPlayerColor(killer, true) + $"(<b>{UtilsRoleText.GetTrueRoleName(killer.PlayerId, false)}</b>)"));
    }
    public override void OverrideTrueRoleName(ref Color roleColor, ref string roleText)
    {
        // 陣営変化前なら上書き不要
        if (Team == TeamType.None)
        {
            return;
        }
        roleColor = DisplayRoleColor;
    }
    public override void OnExileWrapUp(NetworkedPlayerInfo exiled, ref bool DecidedWinner)
    {
        if (exiled.PlayerId != Player.PlayerId || Team != TeamType.None || !ChangeTeamWhenExile)
        {
            return;
        }
        ChangeTeamRandomly();
    }
    /// <summary>
    /// ゲームに存在している陣営の中からランダムに自分の陣営を変更する
    /// </summary>
    private void ChangeTeamRandomly()
    {
        var rand = IRandom.Instance;
        List<TeamType> candidates = new(4)
        {
            TeamType.Crew,
            TeamType.Mad,
        };
        if (CustomRoles.Egoist.IsPresent())
        {
            candidates.Add(TeamType.Egoist);
        }
        if (CustomRoles.Jackal.IsPresent() || CustomRoles.JackalMafia.IsPresent() || CustomRoles.JackalAlien.IsPresent() || CustomRoles.JackalWolf.IsPresent())
        {
            candidates.Add(TeamType.Jackal);
        }
        if (CustomRoles.PavlovDog.IsPresent() || CustomRoles.PavlovOwner.IsPresent() || CustomRoles.PavlovDogImprint.IsPresent())
        {
            candidates.Add(TeamType.Pavlov);
        }
        var team = candidates[rand.Next(candidates.Count)];
        RpcSetTeam(team);
    }
    public bool CheckWin(ref CustomRoles winnerRole)
    {
        bool? won = Team switch
        {
            TeamType.None => CustomWinnerHolder.winners.Contains(CustomWinner.Crewmate) && CanWinTheCrewmateBeforeChange,
            TeamType.Mad => CustomWinnerHolder.winners.Contains(CustomWinner.Impostor),
            TeamType.Crew => CustomWinnerHolder.winners.Contains(CustomWinner.Crewmate),
            TeamType.Jackal => CustomWinnerHolder.winners.Contains(CustomWinner.Jackal),
            TeamType.Egoist => CustomWinnerHolder.winners.Contains(CustomWinner.Egoist),
            TeamType.CountKiller => CustomWinnerHolder.winners.Contains(CustomWinner.CountKiller),
            TeamType.Remotekiller => CustomWinnerHolder.winners.Contains(CustomWinner.Remotekiller),
            TeamType.DoppelGanger => CustomWinnerHolder.winners.Contains(CustomWinner.DoppelGanger),
            TeamType.MilkyWay => CustomWinnerHolder.winners.Contains(CustomWinner.MilkyWay),
            TeamType.Betrayer => CustomWinnerHolder.winners.Contains(CustomWinner.MadBetrayer),
            TeamType.Pavlov => CustomWinnerHolder.winners.Contains(CustomWinner.Pavlov),
            _ => null,
        };
        if (!won.HasValue)
        {
            logger.Warn($"不明な猫の勝利チェック: {Team}");
            return false;
        }
        if (won.Value && Player.IsAlive())
        {
            Achievements.RpcCompleteAchievement(Player.PlayerId, 0, achievements[0]);
        }
        return won.Value;
    }
    public void RpcSetTeam(TeamType team)
    {
        Team = team;
        if (AmongUsClient.Instance.AmHost)
        {
            using var sender = CreateSender();
            sender.Writer.Write((byte)team);
        }
    }
    public override void ReceiveRPC(MessageReader reader)
    {
        Team = (TeamType)reader.ReadByte();
    }

    // マッド属性化までの間マッド状態時に特別扱いするための応急処置的個別実装
    // マッドが属性化したらマッド状態のシュレ猫にマッド属性を付与することで削除
    // 上にあるApplyMadCatOptions，MeetingHudPatchにある道連れ処理，ShipStatusPatchにあるサボ直しキャンセル処理も同様 - Hyz-sui
    public bool? CheckSeeDeathReason(PlayerControl seen) => AmMadmate && Options.MadmateCanSeeDeathReason.GetBool();
    public bool? CheckKillFlash(MurderInfo info) => AmMadmate && Options.MadmateCanSeeKillFlash.GetBool();

    public static Color GetCatColor(TeamType catType)
    {
        Color? color = catType switch
        {
            TeamType.None => RoleInfo.RoleColor,
            TeamType.Mad => UtilsRoleText.GetRoleColor(CustomRoles.Madmate),
            TeamType.Crew => UtilsRoleText.GetRoleColor(CustomRoles.Crewmate),
            TeamType.Jackal => UtilsRoleText.GetRoleColor(CustomRoles.Jackal),
            TeamType.Egoist => UtilsRoleText.GetRoleColor(CustomRoles.Egoist),
            TeamType.Remotekiller => UtilsRoleText.GetRoleColor(CustomRoles.Remotekiller),
            TeamType.CountKiller => UtilsRoleText.GetRoleColor(CustomRoles.CountKiller),
            TeamType.DoppelGanger => UtilsRoleText.GetRoleColor(CustomRoles.DoppelGanger),
            TeamType.MilkyWay => StringHelper.CodeColor(Vega.TeamColor),
            TeamType.Betrayer => UtilsRoleText.GetRoleColor(CustomRoles.MadBetrayer),
            TeamType.Pavlov => UtilsRoleText.GetRoleColor(CustomRoles.PavlovDog),
            _ => null,
        };
        if (!color.HasValue)
        {
            logger.Warn($"不明な猫に対する色の取得: {catType}");
            return UtilsRoleText.GetRoleColor(CustomRoles.Crewmate);
        }
        return color.Value;
    }
    public bool CanUseSabotageButton() => OptionCanUseSabotage.GetBool() && Team != TeamType.None;
    public bool CanUseImpostorVentButton() => OptionCanVent.GetBool() && Team != TeamType.None;
    public bool CanUseKillButton() => Team != TeamType.None && CanKill && HasKillbutton;
    public float CalculateKillCooldown() => OptionKillCooldown.GetFloat();
    public override void CheckWinner(GameOverReason reason)
    {
        if (reason is GameOverReason.ImpostorsBySabotage && Team is TeamType.Mad
            && Main.SabotageType is SystemTypes.Reactor or SystemTypes.Laboratory
            && CustomWinnerHolder.winners.Contains(CustomWinner.Impostor))
        {
            Achievements.RpcCompleteAchievement(Player.PlayerId, 0, achievements[1]);
        }
    }
    public static Dictionary<int, Achievement> achievements = new();
    [Attributes.PluginModuleInitializer]
    public static void Load()
    {
        var n1 = new Achievement(RoleInfo, 0, 1, 0, 0);
        var sp = new Achievement(RoleInfo, 1, 1, 0, 2, true);
        achievements.Add(0, n1);
        achievements.Add(1, sp);
    }
}
