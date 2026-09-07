using Multiplayer.Common;
using Xunit;

public class ServerStartupPlanTests
{
    [Fact]
    public void Save_present_is_loaded_even_when_settings_exist()
    {
        // Регрессия: раньше наличие settings.toml уводило сервер в bootstrap,
        // и он игнорировал собственный save.zip, ожидая загрузки мира от клиента.
        Assert.Equal(ServerStartupAction.LoadSave,
            ServerStartupPlan.Decide(settingsPresent: true, savePresent: true));
    }

    [Fact]
    public void Save_present_without_settings_is_loaded()
    {
        Assert.Equal(ServerStartupAction.LoadSave,
            ServerStartupPlan.Decide(settingsPresent: false, savePresent: true));
    }

    [Fact]
    public void Missing_save_waits_for_a_client_to_upload_the_world()
    {
        Assert.Equal(ServerStartupAction.Bootstrap,
            ServerStartupPlan.Decide(settingsPresent: true, savePresent: false));
    }

    [Fact]
    public void Missing_save_and_missing_settings_still_bootstraps_instead_of_crashing()
    {
        // Раньше эта пара давала BootstrapMode=false и падение в LoadSave
        // с FileNotFoundException на несуществующем save.zip.
        Assert.Equal(ServerStartupAction.Bootstrap,
            ServerStartupPlan.Decide(settingsPresent: false, savePresent: false));
    }
}
