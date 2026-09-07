namespace Multiplayer.Common
{
    /// <summary>
    /// Выбор игрока, чьё мнение о синхронизации сервер принимает за опорное.
    ///
    /// Мнение нужно ровно одно: сервер рассылает его остальным, и каждый
    /// сравнивает себя с ним. Два опорных мнения означали бы, что клиенты
    /// сравниваются с разными эталонами, ноль — что не сравниваются вовсе.
    ///
    /// Выбор вынесен из обработчика пакета, потому что ошибка здесь тихая:
    /// сервер работает, игра идёт, а десинки просто перестают обнаруживаться.
    /// </summary>
    public static class DesyncReference
    {
        /// <param name="ArbiterPlaying">Арбитр подключён и играет.</param>
        /// <param name="HostPresent">Среди играющих есть хост. На автономном
        /// сервере хоста-игрока нет: hostUsername там не присваивается.</param>
        public readonly record struct ServerState(bool ArbiterPlaying, bool HostPresent);

        /// <param name="IsFirstPlaying">Первый играющий по идентификатору.
        /// Порядок должен быть устойчивым, иначе опора будет прыгать между
        /// игроками и сравнение потеряет смысл.</param>
        public readonly record struct Player(bool IsArbiter, bool IsHost, bool IsFirstPlaying);

        /// <summary>
        /// Опорный ли это игрок. Арбитр главнее хоста, хост — первого
        /// играющего; последний нужен для автономного сервера, где нет ни
        /// того, ни другого.
        /// </summary>
        public static bool IsReference(ServerState server, Player player)
        {
            if (server.ArbiterPlaying)
                return player.IsArbiter;

            if (server.HostPresent)
                return player.IsHost;

            return player.IsFirstPlaying;
        }
    }
}
