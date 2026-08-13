/*
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using Il2CppSystem.Runtime.InteropServices;
using MS.Internal.Xml.XPath;
using TownOfHost.Modules;
using TownOfHost.Patches;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using UnityEngine;
using UnityEngineInternal;
using static Il2CppSystem.Globalization.CultureInfo;
using static UnityEngine.UI.GridLayoutGroup;

namespace TownOfHost.Roles.Impostor;

public enum EvilTrapperTrapType { Speed, Slow, Notify }

// Memo
// モノクラー復活したらID変更

public sealed class EvilTrapper : RoleBase, IImpostor, IUsePhantomButton
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(EvilTrapper),
            player => new EvilTrapper(player),
            CustomRoles.EvilTrapper,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            15100, 
            SetupOptionItem,
            "etr",
            OptionSort: (1, 8),
            from: From.NebulaontheShip
        );

    public EvilTrapper(PlayerControl player)
        : base(RoleInfo, player)
    {
        MaxTraps = OptionMaxTraps.GetInt();
        PlaceCooldown = OptionPlaceCooldown.GetFloat();
        TrapRange = OptionTrapRange.GetFloat();
        EffectDuration = OptionEffectDuration.GetFloat();
        SpeedBoost = OptionSpeedBoost.GetFloat();
        SpeedDown = OptionSpeedDown.GetFloat();

        traps = new();
        placedCount = 0;
        cooldownTimer = PlaceCooldown;
        currentTrapType = EvilTrapperTrapType.Speed;
        trapTypeTimer = 0f;
    }

    static OptionItem OptionMaxTraps;
    static OptionItem OptionPlaceCooldown;
    static OptionItem OptionTrapRange;
    static OptionItem OptionEffectDuration;
    static OptionItem OptionSpeedBoost;
    static OptionItem OptionSpeedDown;
    
    static int MaxTraps;
    static float PlaceCooldown;
    static float TrapRange;
    static float EffectDuration;
    static float SpeedBoost;
    static float SpeedDown;

    static bool showing;

    enum OptionName
    {
        NiceTrapperMaxTraps,
        NiceTrapperPlaceCooldown,
        NiceTrapperTrapRange,
        NiceTrapperEffectDuration,
        NiceTrapperSpeedBoost,
        NiceTrapperSpeedDown,
    }

    static void SetupOptionItem()
    {
        OptionMaxTraps = IntegerOptionItem.Create(RoleInfo, 10, OptionName.NiceTrapperMaxTraps,
            new(1, 10, 1), 3, false).SetValueFormat(OptionFormat.Times);
        OptionPlaceCooldown = FloatOptionItem.Create(RoleInfo, 11, OptionName.NiceTrapperPlaceCooldown,
            new(0f, 60f, 2.5f), 15f, false).SetValueFormat(OptionFormat.Seconds);
        OptionTrapRange = FloatOptionItem.Create(RoleInfo, 12, OptionName.NiceTrapperTrapRange,
            new(0.3f, 3f, 0.1f), 1.0f, false).SetValueFormat(OptionFormat.Multiplier);
        OptionEffectDuration = FloatOptionItem.Create(RoleInfo, 13, OptionName.NiceTrapperEffectDuration,
            new(1f, 30f, 1f), 5f, false).SetValueFormat(OptionFormat.Seconds);
        OptionSpeedBoost = FloatOptionItem.Create(RoleInfo, 14, OptionName.NiceTrapperSpeedBoost,
            new(1.1f, 3f, 0.1f), 1.5f, false).SetValueFormat(OptionFormat.Multiplier);
        OptionSpeedDown = FloatOptionItem.Create(RoleInfo, 15, OptionName.NiceTrapperSpeedDown,
            new(0.1f, 0.9f, 0.1f), 0.5f, false).SetValueFormat(OptionFormat.Multiplier);
    }

    class TrapData
    {
        public TrapNetObject Obj;
        public EvilTrapperTrapType Type;
        public bool Active;
        public Vector2 Position;
        public HashSet<byte> PlayersInRange = new();
    }

    readonly List<TrapData> traps;
    int placedCount;
    float cooldownTimer;

    EvilTrapperTrapType currentTrapType;
    float trapTypeTimer;

    readonly Dictionary<byte, float> effectTimers = new();
    readonly Dictionary<byte, float> savedSpeeds = new();
    readonly List<(Vector2 pos, string colorCode)> activeNotifyArrows = new();

    bool IUsePhantomButton.IsPhantomRole => true;
    bool IUsePhantomButton.IsresetAfterKill => false;
    bool IUsePhantomButton.UseOneclickButton => true;

    void IUsePhantomButton.OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = false;
        ResetCooldown = false;

        if (!Player.IsAlive()) return;

        if (!AmongUsClient.Instance.AmHost)
        {
            return;
        }

        OnPetAction();
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.PhantomCooldown = cooldownTimer > 0f ? cooldownTimer : 0.1f;
    }


    public override void Add()
    {
        placedCount = 0;
        cooldownTimer = PlaceCooldown;
        currentTrapType = EvilTrapperTrapType.Speed;
        trapTypeTimer = 0f;
        traps.Clear();
        effectTimers.Clear();
        savedSpeeds.Clear();
        activeNotifyArrows.Clear();
        PetActionManager.Register(Player.PlayerId, OnPetAction);
    }

    public override void OnSpawn(bool initialState = false)
    {
        cooldownTimer = PlaceCooldown + 1.5f;
        Player.RpcResetAbilityCooldown(Sync: true);
    }

    public override void OnDestroy()
    {
        PetActionManager.Unregister(Player.PlayerId);
        DespawnAll();
    }

    public override bool CanClickUseVentButton => false;
    public override bool OnEnterVent(PlayerPhysics physics, int ventId) => false;

    void OnPetAction()
    {
        if (!Player.IsAlive()) return;
        if (placedCount >= MaxTraps) return;
        if (cooldownTimer > 0f) return;
        if (!AmongUsClient.Instance.AmHost) return;
        PlaceTrap(Player.transform.position);
    }

    void PlaceTrap(Vector2 pos)
    {
        var data = new TrapData
        {
            Type = currentTrapType,
            Active = false,
            Position = pos,
            Obj = new TrapNetObject(pos, currentTrapType, Player, activated: false, breaked: false)
        };
        traps.Add(data);

        placedCount++;
        cooldownTimer = PlaceCooldown;
        Player.RpcResetAbilityCooldown(Sync: true);

        SendRpc();
        UtilsNotifyRoles.NotifyRoles(OnlyMeName: true);
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!GameStates.IsInTask || GameStates.IsMeeting) return;

        if (!Player.IsAlive() && traps.Count > 0)
        {
            DespawnAll();
            return;
        }

        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.fixedDeltaTime;
            if (cooldownTimer < 0f) cooldownTimer = 0f;
        }

        trapTypeTimer += Time.fixedDeltaTime;
        if (trapTypeTimer >= 3f)
        {
            trapTypeTimer = 0f;
            currentTrapType = (EvilTrapperTrapType)(((int)currentTrapType + 1) % 3);
            UtilsNotifyRoles.NotifyRoles(OnlyMeName: true);
        }

        foreach (var pid in effectTimers.Keys.ToArray())
        {
            effectTimers[pid] -= Time.fixedDeltaTime;
            if (effectTimers[pid] <= 0f)
            {
                RemoveEffect(pid);
                effectTimers.Remove(pid);
            }
        }

        foreach (var trap in traps.ToArray())
        {
            if (!trap.Active || trap.Obj == null) continue;

            var nowInRange = new HashSet<byte>();
            foreach (var pc in PlayerCatch.AllAlivePlayerControls)
            {
                if (Vector2.Distance(pc.transform.position, trap.Position) > TrapRange) continue;
                nowInRange.Add(pc.PlayerId);
                if (!trap.PlayersInRange.Contains(pc.PlayerId))
                    TriggerTrap(trap, pc);
            }
            trap.PlayersInRange = nowInRange;
        }
    }

    void TriggerTrap(TrapData trap, PlayerControl target)
    {
        switch (trap.Type)
        {
            case EvilTrapperTrapType.Speed: ApplySpeedEffect(target, SpeedBoost); break;
            case EvilTrapperTrapType.Slow: ApplySpeedEffect(target, SpeedDown); break;
            case EvilTrapperTrapType.Notify: NotifyTrapper(trap, target); break;
        }
    }

    void ApplySpeedEffect(PlayerControl target, float multiplier)
    {
        byte id = target.PlayerId;
        if (!savedSpeeds.ContainsKey(id))
            savedSpeeds[id] = Main.AllPlayerSpeed.TryGetValue(id, out float s) ? s : 1f;
        Main.AllPlayerSpeed[id] = savedSpeeds[id] * multiplier;
        target.MarkDirtySettings();
        effectTimers[id] = EffectDuration;
    }

    void RemoveEffect(byte playerId)
    {
        if (!savedSpeeds.TryGetValue(playerId, out float orig)) return;
        Main.AllPlayerSpeed[playerId] = orig;
        PlayerCatch.GetPlayerById(playerId)?.MarkDirtySettings();
        savedSpeeds.Remove(playerId);
    }

    void NotifyTrapper(TrapData trap, PlayerControl target)
    {
        PlayerState.GetByPlayerId(target.PlayerId).DeathReason = CustomDeathReason.Spell;
        target.RpcMurderPlayer(target);
        traps.Remove(trap);
        trap.Obj = new TrapNetObject(trap.Position, trap.Type, Player, activated: true, breaked: true);
    }

    public override void OnStartMeeting()
    {
        foreach (var pid in effectTimers.Keys.ToArray()) RemoveEffect(pid);
        effectTimers.Clear();
        foreach (var trap in traps) trap.PlayersInRange.Clear();
        activeNotifyArrows.Clear();
    }

    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost) return;

        for (int i = 0; i < traps.Count; i++)
        {
            var trap = traps[i];
            var pos = trap.Position;
            var type = trap.Type;
            int idx = i;
            var old = trap.Obj;
            bool breaked = trap.Obj.breaked_;

            _ = new LateTask(() =>
            {
                try { old?.Despawn(); } catch { }
                trap.Active = true;
                trap.Obj = new TrapNetObject(pos, type, Player, activated: true, breaked);
            }, idx * 0.6f + 1.0f, $"EvilTrapper.Activate.{idx}", true);
        }

        cooldownTimer = PlaceCooldown;
        Player.RpcResetAbilityCooldown(Sync: true);
        SendRpc();
    }

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        foreach (var pid in effectTimers.Keys.ToArray()) RemoveEffect(pid);
        effectTimers.Clear();
        foreach (var trap in traps) trap.PlayersInRange.Clear();
    }

    public override void OnMurderPlayerAsTarget(MurderInfo info) => DespawnAll();

    void DespawnAll()
    {
        foreach (var trap in traps.ToArray())
            try { trap.Obj?.Despawn(); } catch { }
        traps.Clear();
    }

    public override string GetMark(PlayerControl seer, PlayerControl seen = null,
        bool isForMeeting = false)
    {
        seen ??= seer;
        if (isForMeeting || !Player.IsAlive() || !Is(seer) || !Is(seen)) return "";

        var arrows = "";
        foreach (var arrowData in activeNotifyArrows.ToArray())
        {
            var arr = GetArrow.GetArrows(seer, arrowData.pos);
            if (!string.IsNullOrEmpty(arr))
                arrows += $"<color={arrowData.colorCode}>{arr}</color>";
        }
        return arrows;
    }

    public override string GetProgressText(bool comms = false, bool GameLog = false)
    {
        if (!Player.IsAlive()) return "";

        string typeIcon = currentTrapType switch
        {
            EvilTrapperTrapType.Speed => "<color=#4488ff>▲</color>",
            EvilTrapperTrapType.Slow => "<color=#ff4444>▼</color>",
            EvilTrapperTrapType.Notify => "<color=#ffff00>●</color>",
            _ => "?"
        };
        return $"<color={RoleInfo.RoleColorCode}>({MaxTraps - placedCount}残)</color>{typeIcon}";
    }

    void SendRpc()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        using var sender = CreateSender();
        sender.Writer.Write(placedCount);
        sender.Writer.Write(cooldownTimer);
        sender.Writer.Write((int)currentTrapType);
        sender.Writer.Write(traps.Count);
        foreach (var t in traps)
        {
            sender.Writer.Write((int)t.Type);
            sender.Writer.Write(t.Active);
            sender.Writer.Write(t.Position.x);
            sender.Writer.Write(t.Position.y);
        }
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        placedCount = reader.ReadInt32();
        cooldownTimer = reader.ReadSingle();
        currentTrapType = (EvilTrapperTrapType)reader.ReadInt32();
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            reader.ReadInt32();
            reader.ReadBoolean();
            reader.ReadSingle();
            reader.ReadSingle();
        }
    }
}

public sealed class TrapNetObject : CustomNetObject
{
    static readonly int[] TrapColorIds = { 1, 0, 5 };

    readonly EvilTrapperTrapType _type;
    readonly PlayerControl _owner;
    readonly Vector2 _pos;
    readonly bool _activated;
    readonly bool _breaked;
    internal bool breaked_;

    public TrapNetObject(Vector2 position, EvilTrapperTrapType type,
        PlayerControl owner, bool activated, bool breaked)
    {
        _type = type;
        _owner = owner;
        _pos = position;
        _activated = activated;
        _breaked = breaked;
        breaked_ = _breaked;
        CreateNetObject(position);
    }
    protected override void OnCreated()
    {
        if (PlayerControl == null) return;

        var hostPlayer = PlayerControl.LocalPlayer;
        byte hostColor = (byte)(hostPlayer?.Data?.DefaultOutfit.ColorId ?? 0);
        int trapColor = TrapColorIds[(int)_type];

        PlayerControl.RpcSetColor((byte)trapColor);
        if (hostPlayer != null)
            hostPlayer.RpcSetColor(hostColor);
        PlayerControl.RawSetColor((byte)trapColor);

        string label = _type switch
        {
            EvilTrapperTrapType.Speed => "<color=#4488ff>▲</color>",
            EvilTrapperTrapType.Slow => "<color=#ff4444>▼</color>",
            EvilTrapperTrapType.Notify => "<color=#ffff00>●</color>",
            _ => "?"
        };
        SetName(label);
        SnapToPosition(_pos);

        var capturedPC = PlayerControl;
        _ = new LateTask(() =>
        {
            if (capturedPC != null) capturedPC.RawSetColor((byte)trapColor);
        }, 0.15f, "EvilTrapper.ApplyColor", true);

        bool showAll = true;
        if (_type != EvilTrapperTrapType.Notify)
        {
            if (!_activated)
            {
                showAll = false;
            }
        }
        if (_type == EvilTrapperTrapType.Notify)
        {
            if (!_activated && !_breaked)
            {
                showAll = false;
            }
        }
        if (!showAll)
        {
            foreach (var pc in PlayerCatch.AllPlayerControls)
            {
                if (pc.notRealPlayer) continue;
                if (pc.PlayerId != _owner.PlayerId)
                    Hide(pc);
            }
        }

    }

    public override void OnMeeting() { }
}
*/