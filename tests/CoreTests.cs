using QuickWheel.Models;
using QuickWheel.Services;
using Xunit;

namespace QuickWheel.Tests;

public class KeyParserTests
{
    [Fact]
    public void Combo_et_sequence()
    {
        var steps = KeyParser.Parse("Ctrl+K, Ctrl+C");
        Assert.Equal(2, steps.Count);
        Assert.Equal(new[] { KeyParser.VK_CONTROL, 'K' }, steps[0].Keys);
        Assert.Equal(new[] { KeyParser.VK_CONTROL, 'C' }, steps[1].Keys);
    }

    [Fact]
    public void Ctrl_plus_plus_donne_la_touche_plus()
    {
        var step = Assert.Single(KeyParser.Parse("Ctrl++"));
        Assert.Equal(new[] { KeyParser.VK_CONTROL, 0xBB }, step.Keys);
    }

    [Theory]
    [InlineData("wait 200")]
    [InlineData("{200}")]
    public void Pause(string text) => Assert.Equal(200, Assert.Single(KeyParser.Parse(text)).WaitMs);

    [Fact]
    public void Touche_inconnue_refusee() => Assert.Throws<FormatException>(() => KeyParser.Parse("Ctrl+Truc"));

    [Fact]
    public void Aller_retour_texte()
    {
        foreach (var vk in new[] { 'A', '7', 0x70, 0x0D, 0x26, 0x2E })
            Assert.Equal(vk, KeyParser.ParseKey(KeyParser.FormatKey(vk)));
    }
}

public class WheelLogicTests
{
    private static WheelLayout Layout(params string[][] rings)
    {
        var l = new WheelLayout();
        foreach (var r in rings) l.Rings.Add(new Ring { Items = new(r) });
        return l;
    }

    [Fact]
    public void Deplacer_le_dernier_d_un_anneau_supprime_l_anneau()
    {
        var l = Layout(new[] { "a", "b" }, new[] { "c" });
        WheelLogic.Move(l, "c", new DropTarget(0, 1, false));
        Assert.Single(l.Rings);
        Assert.Equal(new[] { "a", "c", "b" }, l.Rings[0].Items);
    }

    [Fact]
    public void Nouvel_anneau()
    {
        var l = Layout(new[] { "a", "b" });
        WheelLogic.Move(l, "a", new DropTarget(1, 0, true));
        Assert.Equal(2, l.Rings.Count);
        Assert.Equal(new[] { "a" }, l.Rings[1].Items);
    }

    [Fact]
    public void Une_app_ne_peut_pas_avoir_deux_roues()
    {
        var cfg = new AppConfig();
        cfg.Apps.Add(new AppTarget { Name = "Chrome", ProcessNames = "chrome" });
        cfg.Apps.Add(new AppTarget { Name = "Web", ProcessNames = "msedge, Chrome.exe" });
        Assert.Single(WheelLogic.ProcessClashes(cfg));
    }

    [Fact]
    public void Roue_de_l_app_sinon_globale()
    {
        var cfg = WheelLogic.CreateDefault();
        var app = new AppTarget { Name = "Calc", ProcessNames = "CalculatorApp" };
        cfg.Apps.Add(app);
        Assert.Same(app, WheelLogic.ForProcess(cfg, "calculatorapp").App);
        Assert.Null(WheelLogic.ForProcess(cfg, "notepad").App);
    }

    [Fact]
    public void Import_refuse_une_app_qui_a_deja_sa_roue()
    {
        var cfg = new AppConfig();
        var app = new AppTarget { Name = "Chrome", ProcessNames = "chrome" };
        cfg.Apps.Add(app);
        var json = ConfigStore.ExportApp(cfg, app);
        Assert.Throws<InvalidOperationException>(() => ConfigStore.ImportApp(cfg, json));
    }

    [Fact]
    public void Import_oublie_les_images_absentes_de_ce_pc()
    {
        var src = new AppConfig();
        var app = new AppTarget { Name = "Chrome", ProcessNames = "chrome", Icon = @"img:C:\nexiste\pas.png" };
        src.Apps.Add(app);
        var imported = ConfigStore.ImportApp(new AppConfig(), ConfigStore.ExportApp(src, app));
        Assert.Null(imported.Icon);
    }

    [Fact]
    public void Import_complet_oublie_aussi_les_images_absentes()
    {
        var cfg = new AppConfig();
        cfg.Apps.Add(new AppTarget { Name = "Chrome", ProcessNames = "chrome", Icon = @"img:C:\nexiste\pas.png" });
        var folder = new Shortcut { Kind = ShortcutKind.Folder };
        folder.Folder.Items.Add(new Shortcut { Icon = @"img:C:\nexiste\non.png" });
        cfg.Shortcuts.Add(folder);
        ConfigStore.DropMissingImages(cfg);
        Assert.Null(cfg.Apps[0].Icon);
        Assert.Equal(new Shortcut().Icon, folder.Folder.Items[0].Icon);
    }

    [Fact]
    public void Icone_nulle_ecrite_a_la_main_ne_casse_pas_la_config()
    {
        var cfg = ConfigStore.Deserialize("""{ "Version": 2, "Shortcuts": [ { "Label": "A", "Icon": null } ] }""");
        Assert.Equal(new Shortcut().Icon, Assert.Single(cfg.Shortcuts).Icon);
    }

    [Fact]
    public async Task Deux_raccourcis_ne_se_melangent_pas()
    {
        Shortcut Pause() { var s = new Shortcut(); s.Actions.Add(new ActionStep { Type = ActionType.Wait, Value = "150" }); return s; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Task.WhenAll(ActionRunner.RunAsync(Pause(), null), ActionRunner.RunAsync(Pause(), null));
        Assert.True(sw.ElapsedMilliseconds >= 300, $"{sw.ElapsedMilliseconds} ms : les deux ont tourné en même temps");
    }
}
