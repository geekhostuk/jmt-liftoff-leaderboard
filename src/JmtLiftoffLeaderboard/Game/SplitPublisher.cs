using System;
using System.Linq;
using Photon.Pun;

namespace JmtLiftoffLeaderboard.Game;

/// <summary>
/// Shares the gate times of the pilot's own laps with the room they're flying in, on their
/// Photon player (see <see cref="RoomSplitCodec"/>). In a JMT room, the host's race mod reads
/// them, pairs each with the lap the room recorded for this pilot, and its Liftoff Control
/// sends them to the JMT site with the room's laps. Nothing to link: they go under the id the
/// room already knows the pilot by.
///
/// Only whole laps measured from the line through every gate, and only what the race mod
/// would take. Anyone in the room could read them, as they can the lap times the game shares.
/// </summary>
internal sealed class SplitPublisher
{
    private readonly Settings _settings;
    // The gates last put on the player, and the room and player they went to: a new reader
    // gets them at least once per room.
    private string[]? _gates;
    private string? _sentTo;
    private int _seq;
    private int _shared;
    private bool _complained;

    public SplitPublisher(Settings settings, DeltaTracker delta)
    {
        _settings = settings;
        // A reader drops a lap whose number it has just seen from this player, so a restarted
        // game starts from the clock rather than from nothing.
        _seq = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() & 0x3FFFFFFF);
        delta.LapFinished += OnLap;
    }

    private void OnLap(FlownLap lap, long? boardId, int? prevLapMs)
    {
        if (!_settings.SplitsShareWithRoom.Value || !PhotonNetwork.InRoom)
            return;
        if (lap.LapMs is not { } lapMs || !lap.Measured || !RoomSplitCodec.CanShare(lap.Gates, lap.Times, lapMs))
            return;
        try
        {
            var player = PhotonNetwork.LocalPlayer;
            var sentTo = $"{PhotonNetwork.CurrentRoom?.Name}#{player.ActorNumber}";
            var props = new ExitGames.Client.Photon.Hashtable();
            if (sentTo != _sentTo || _gates == null || !_gates.SequenceEqual(lap.Gates))
            {
                _gates = lap.Gates.ToArray();
                _sentTo = sentTo;
                props[RoomSplitCodec.GatesKey] = _gates;
            }
            _seq = unchecked(_seq + 1);
            props[RoomSplitCodec.SplitsKey] = RoomSplitCodec.Encode(_seq, lap.Gates, lap.Times, lapMs, prevLapMs);
            player.SetCustomProperties(props);
            if (_shared++ == 0)
                Plugin.Log.LogInfo($"HUD: sharing your gate times with the room ({lap.Gates.Count} gates); a JMT room's host sends them to the site.");
        }
        catch (Exception ex)
        {
            if (!_complained)
                Plugin.Log.LogWarning($"Couldn't share your gate times with the room: {ex.Message}");
            _complained = true;
        }
    }
}
