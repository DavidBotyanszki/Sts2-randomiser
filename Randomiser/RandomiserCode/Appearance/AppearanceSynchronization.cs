using System.Runtime.CompilerServices;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

namespace Randomiser.RandomiserCode.Appearance;

public static class AppearanceSynchronization
{
    private sealed class SyncState
    {
        public TaskCompletionSource Ready { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public HashSet<ulong> Players { get; } = [];
        public bool Requested { get; set; }
    }

    private static readonly ConditionalWeakTable<IRunState, SyncState> States = new();

    public static void Publish(IRunState run, INetGameService service)
    {
        if (service.Type != NetGameType.Host || !service.IsConnected)
            return;
        var message = new AppearanceSyncMessage { RunSeed = run.Rng.StringSeed };
        foreach (var player in run.Players)
        {
            CombatAppearance.Roll(player);
            message.Models.Add((player.NetId, CombatAppearance.GetSavedModel(player)));
        }
        CustomMessageWrapper.Send(message, service);
        SyncState sync = States.GetValue(run, _ => new SyncState());
        sync.Players.Clear();
        sync.Players.UnionWith(message.Models.Select(entry => entry.PlayerId));
        sync.Ready.TrySetResult();
        MainFile.Logger.Info($"Host published combat appearances for {message.Models.Count} players.");
    }

    public static async Task EnsureReady(IRunState run)
    {
        INetGameService? service = RunManager.Instance?.NetService;
        if (service == null || service.Type is NetGameType.Singleplayer or NetGameType.Replay)
            return;
        if (service.Type == NetGameType.Host)
        {
            Publish(run, service);
            return;
        }
        if (service.Type != NetGameType.Client || !service.IsConnected)
            return;

        SyncState sync = States.GetValue(run, _ => new SyncState());
        if (sync.Ready.Task.IsCompleted && sync.Players.SetEquals(run.Players.Select(player => player.NetId)))
            return;
        if (sync.Ready.Task.IsCompleted)
        {
            // A new player can join an existing run after its first snapshot.
            sync.Ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            sync.Requested = false;
        }
        if (!sync.Requested)
        {
            sync.Requested = true;
            CustomMessageWrapper.Send(new AppearanceSyncMessage
                { RunSeed = run.Rng.StringSeed, IsRequest = true }, service);
        }

        // These messages bypass the game's loading buffer. Never wait forever
        // for a peer running an incompatible version or for a disconnected host.
        Task completed = await Task.WhenAny(sync.Ready.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        if (completed != sync.Ready.Task)
        {
            sync.Requested = false;
            MainFile.Logger.Warn("Timed out waiting for host combat appearances; retaining received/saved visuals and retrying next load.");
        }
    }

    public static void Receive(AppearanceSyncMessage message, ulong senderId)
    {
        var manager = RunManager.Instance;
        IRunState? run = manager?.DebugOnlyGetState();
        INetGameService? service = manager?.NetService;
        if (run == null || service == null || !service.IsConnected || message.RunSeed != run.Rng.StringSeed)
            return;
        if (message.IsRequest)
        {
            if (service.Type == NetGameType.Host && run.Players.Any(player => player.NetId == senderId))
                Publish(run, service);
            return;
        }
        if (service is not NetClientGameService client || senderId != client.HostNetId)
            return;
        if (message.Models.Count != run.Players.Count || message.Models.Select(entry => entry.PlayerId).Distinct().Count() != message.Models.Count
            || message.Models.Any(entry => !run.Players.Any(player => player.NetId == entry.PlayerId) || !entry.ModelId.Contains('/')))
        {
            MainFile.Logger.Warn("Ignored incomplete host combat appearance snapshot.");
            return;
        }

        foreach (var (playerId, modelId) in message.Models)
            CombatAppearance.SetAuthoritativeModel(run.Players.Single(player => player.NetId == playerId), modelId);
        SyncState sync = States.GetValue(run, _ => new SyncState());
        sync.Players.Clear();
        sync.Players.UnionWith(message.Models.Select(entry => entry.PlayerId));
        sync.Ready.TrySetResult();
        MainFile.Logger.Info($"Applied host combat appearances for {message.Models.Count} players.");
    }
}

// BaseLib discovers ICustomMessage implementations when it registers network
// handlers. This protocol communicates model IDs, never a client-local pool index.
public sealed class AppearanceSyncMessage : ICustomMessage
{
    public string RunSeed { get; set; } = "";
    public bool IsRequest { get; set; }
    public List<(ulong PlayerId, string ModelId)> Models { get; } = [];
    public bool ShouldBroadcast => false;
    public bool ShouldBuffer => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;

    public void HandleMessage(ulong senderId) => AppearanceSynchronization.Receive(this, senderId);

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(RunSeed);
        writer.WriteBool(IsRequest);
        writer.WriteInt(Models.Count, 32);
        foreach (var (playerId, modelId) in Models)
        {
            writer.WriteULong(playerId, 64);
            writer.WriteString(modelId);
        }
    }

    public void Deserialize(PacketReader reader)
    {
        RunSeed = reader.ReadString();
        IsRequest = reader.ReadBool();
        int count = reader.ReadInt(32);
        if (count is < 0 or > 64)
            throw new InvalidDataException("Invalid appearance snapshot player count.");
        Models.Clear();
        for (int i = 0; i < count; i++)
            Models.Add((reader.ReadULong(64), reader.ReadString()));
    }
}
