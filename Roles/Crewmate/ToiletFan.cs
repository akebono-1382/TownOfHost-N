using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Patches;
using TownOfHost.Roles.Core;
using UnityEngine;

namespace TownOfHost.Roles.Crewmate;

public sealed class ToiletFan : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(ToiletFan),
            player => new ToiletFan(player),
            CustomRoles.ToiletFan,
            () => RoleTypes.Engineer,
            CustomRoleTypes.Crewmate,
            36100,
            SetupOptionItem,
            "to",
            "#5f5573",
            (9, 3),
            introSound: () => GetIntroSound(RoleTypes.Crewmate),
            assignInfo: new RoleAssignInfo(CustomRoles.ToiletFan, CustomRoleTypes.Crewmate)
            {
                // トイレファンをエアシップ限定にするオプションがONのときのみエアシップで初期配役可能にする
                IsInitiallyAssignableCallBack = () => (OptionOnlyAirship?.GetBool() ?? false) ? Main.NormalOptions.MapId is 4 : true
            },
            from: From.SuperNewRoles
        );
    public ToiletFan(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        Cooldown = OptionCooldown.GetFloat();
        flug = 0;
        IsDead = false;
        PetActionManager.Register(Player.PlayerId, OnPet);
        cooldownLeft = 0f;
    }
    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.EngineerCooldown = cooldownLeft > 0f ? cooldownLeft : Cooldown;
        AURoleOptions.EngineerInVentMaxTime = 0f;
    }
    float cooldownLeft;
    private static OptionItem OptionCooldown;
    private static OptionItem OptionOnlyAirship;
    private static OptionItem OptionOpenKilled;
    bool IsDead;
    enum OptionName
    {
        Cooldown,
        OnlyAirship,
        OpenKilled
    }
    private static float Cooldown;
    int flug;
    private static void SetupOptionItem()
    {
        OptionCooldown = FloatOptionItem.Create(RoleInfo, 10, OptionName.Cooldown, new(1f, 30f, 1f), 5f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionOnlyAirship = BooleanOptionItem.Create(RoleInfo, 11, OptionName.OnlyAirship, true, false);
        OptionOpenKilled = BooleanOptionItem.Create(RoleInfo, 12, OptionName.OpenKilled, true, false);
    }
    public override bool OnEnterVent(PlayerPhysics physics, int ventId)
    {
        flug = Main.NormalOptions.MapId is 4 ? 1 : 2;
        ShipStatus.Instance.RpcUpdateSystem(SystemTypes.Doors, 79);
        ShipStatus.Instance.RpcUpdateSystem(SystemTypes.Doors, 80);
        ShipStatus.Instance.RpcUpdateSystem(SystemTypes.Doors, 81);
        ShipStatus.Instance.RpcUpdateSystem(SystemTypes.Doors, 82);
        cooldownLeft = Cooldown;
        return false;
    }
    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!Player.IsAlive()) return;
        if (cooldownLeft <= 0f) return;

        float prev = cooldownLeft;
        cooldownLeft -= Time.fixedDeltaTime;
        if (cooldownLeft < 0f) cooldownLeft = 0f;
        if (Mathf.FloorToInt(prev) != Mathf.FloorToInt(cooldownLeft))
        {
            Player.MarkDirtySettings();
            SendRpc();
        }
    }
    void OnPet()
    {
        if (cooldownLeft > 0f) return;
        if (!Player.IsAlive()) return;
        if (!AmongUsClient.Instance.AmHost) return;
        flug = Main.NormalOptions.MapId is 4 ? 1 : 2;
        ShipStatus.Instance.RpcUpdateSystem(SystemTypes.Doors, 79);
        ShipStatus.Instance.RpcUpdateSystem(SystemTypes.Doors, 80);
        ShipStatus.Instance.RpcUpdateSystem(SystemTypes.Doors, 81);
        ShipStatus.Instance.RpcUpdateSystem(SystemTypes.Doors, 82);
        cooldownLeft = Cooldown;
        SendRpc();
    }
    public override void OnDestroy()
    {
        PetActionManager.Unregister(Player.PlayerId);
    }
    public override string GetAbilityButtonText() => GetString("ToiletFanAbility");
    public override bool OverrideAbilityButton(out string text)
    {
        text = "ToiletFan_Ability";
        return true;
    }
    public override void CheckWinner(GameOverReason reason)
    {
        switch (flug)
        {
            case 1:
                Achievements.RpcCompleteAchievement(Player.PlayerId, 0, achievements[0]);
                break;
            case 2:
                Achievements.RpcCompleteAchievement(Player.PlayerId, 0, achievements[1]);
                break;
            default: break;
        }
    }
    public override void OnMurderPlayerAsTarget(MurderInfo info)
    {
        if (GameStates.IsMeeting || info.IsAccident || info.IsSuicide || !info.CanKill || !info.DoKill || IsDead)
        {
            return;
        }
        if (OptionOpenKilled.GetBool())
        {
            flug = Main.NormalOptions.MapId is 4 ? 1 : 2;
            ShipStatus.Instance.RpcUpdateSystem(SystemTypes.Doors, 79);
            ShipStatus.Instance.RpcUpdateSystem(SystemTypes.Doors, 80);
            ShipStatus.Instance.RpcUpdateSystem(SystemTypes.Doors, 81);
            ShipStatus.Instance.RpcUpdateSystem(SystemTypes.Doors, 82);
        }
    }
    void SendRpc()
    {
        using var sender = CreateSender();
        sender.Writer.Write(cooldownLeft);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        cooldownLeft = reader.ReadSingle();
    }
    public static System.Collections.Generic.Dictionary<int, Achievement> achievements = new();
    [Attributes.PluginModuleInitializer]
    public static void Load()
    {
        var n1 = new Achievement(RoleInfo, 0, 1, 0, 0);
        var n2 = new Achievement(RoleInfo, 1, 1, 0, 0);
        achievements.Add(0, n1);
        achievements.Add(1, n2);
    }
}