namespace Multiplayer.Common
{
    /// <summary>Что сервер делает с миром на старте.</summary>
    public enum ServerStartupAction
    {
        /// <summary>Мир есть на диске — загрузить его.</summary>
        LoadSave,

        /// <summary>Мира нет — ждать, пока его загрузит подключившийся клиент.</summary>
        Bootstrap,
    }

    /// <summary>
    /// Решение о старте вынесено из точки входа, чтобы проверяться тестами:
    /// ошибка здесь тихая — сервер поднимается, но живёт не тем миром.
    /// </summary>
    public static class ServerStartupPlan
    {
        /// <summary>
        /// Bootstrap нужен ровно тогда, когда сервер не знает мира. Наличие
        /// файла настроек к этому отношения не имеет: настройки и мир —
        /// независимые вещи, и связка settings.toml + save.zip обязана
        /// загрузить свой сейв, а не ждать его от клиента.
        /// </summary>
        public static ServerStartupAction Decide(bool settingsPresent, bool savePresent)
        {
            return savePresent ? ServerStartupAction.LoadSave : ServerStartupAction.Bootstrap;
        }
    }
}
