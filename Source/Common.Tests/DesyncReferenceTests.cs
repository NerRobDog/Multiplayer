using Multiplayer.Common;
using Xunit;

public class DesyncReferenceTests
{
    [Fact]
    public void Arbiter_is_the_reference_whenever_it_plays()
    {
        var state = new DesyncReference.ServerState(ArbiterPlaying: true, HostPresent: true);

        Assert.True(DesyncReference.IsReference(state,
            new DesyncReference.Player(IsArbiter: true, IsHost: false, IsFirstPlaying: false)));
        Assert.False(DesyncReference.IsReference(state,
            new DesyncReference.Player(IsArbiter: false, IsHost: true, IsFirstPlaying: true)));
    }

    [Fact]
    public void Host_is_the_reference_when_no_arbiter_plays()
    {
        var state = new DesyncReference.ServerState(ArbiterPlaying: false, HostPresent: true);

        Assert.True(DesyncReference.IsReference(state,
            new DesyncReference.Player(IsArbiter: false, IsHost: true, IsFirstPlaying: false)));
        Assert.False(DesyncReference.IsReference(state,
            new DesyncReference.Player(IsArbiter: false, IsHost: false, IsFirstPlaying: true)));
    }

    [Fact]
    public void Standalone_server_falls_back_to_the_first_playing_player()
    {
        // Регрессия: на standalone-сервере hostUsername не присваивается
        // нигде, IsHost ложно для всех, и прежнее условие отбрасывало каждое
        // мнение — обнаружение десинков не работало вовсе.
        var state = new DesyncReference.ServerState(ArbiterPlaying: false, HostPresent: false);

        Assert.True(DesyncReference.IsReference(state,
            new DesyncReference.Player(IsArbiter: false, IsHost: false, IsFirstPlaying: true)));
        Assert.False(DesyncReference.IsReference(state,
            new DesyncReference.Player(IsArbiter: false, IsHost: false, IsFirstPlaying: false)));
    }

    [Fact]
    public void Reference_is_exactly_one_player_in_every_configuration()
    {
        // Опорное мнение должно быть одно: два опоры означают, что клиенты
        // сравнивают себя с разными эталонами и десинк остаётся незамеченным.
        foreach (var arbiterPlaying in new[] { true, false })
        foreach (var hostPresent in new[] { true, false })
        {
            var state = new DesyncReference.ServerState(arbiterPlaying, hostPresent);
            var players = new[]
            {
                new DesyncReference.Player(IsArbiter: arbiterPlaying, IsHost: false, IsFirstPlaying: false),
                new DesyncReference.Player(IsArbiter: false, IsHost: hostPresent, IsFirstPlaying: false),
                new DesyncReference.Player(IsArbiter: false, IsHost: false, IsFirstPlaying: true),
            };

            var count = 0;
            foreach (var player in players)
                if (DesyncReference.IsReference(state, player))
                    count++;

            Assert.Equal(1, count);
        }
    }
}
