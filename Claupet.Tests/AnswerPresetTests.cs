namespace Claupet.Tests;

/// <summary>Los modos de Ajustes › Respuestas › Quién responde primero.</summary>
public class AnswerPresetTests
{
    [Fact]
    public void Sin_tocar_nada_el_modo_es_Por_defecto()
    {
        Assert.Equal(AnswerPresets.Default, AnswerPresets.Current(new AppSettings()));
    }

    [Fact]
    public void Ahorro_pone_primero_la_app_y_el_modelo_local_y_deja_pasar_a_la_nube()
    {
        var s = new AppSettings();
        AnswerPresets.Apply(s, AnswerPresets.Saving);
        Assert.Equal(AnswerPresets.Saving, AnswerPresets.Current(s));
        Assert.Equal(Engine.None, AnswerTopics.First(s, AnswerTopics.Chat));
        Assert.Equal(Engine.Local, AnswerTopics.First(s, AnswerTopics.General));
        Assert.Equal(Engine.Cloud, AnswerTopics.First(s, AnswerTopics.Internet));
        Assert.Equal(Engine.Cloud, s.AnswerFallback);
    }

    [Fact]
    public void Solo_en_este_equipo_no_pasa_a_la_nube()
    {
        var s = new AppSettings();
        AnswerPresets.Apply(s, AnswerPresets.Private);
        Assert.Equal(AnswerPresets.Private, AnswerPresets.Current(s));
        Assert.Equal(Engine.Local, s.AnswerFallback);
    }

    [Fact]
    public void Volver_a_Por_defecto_deja_todo_como_siempre()
    {
        var s = new AppSettings();
        AnswerPresets.Apply(s, AnswerPresets.Private);
        AnswerPresets.Apply(s, AnswerPresets.Default);
        Assert.Empty(s.AnswerEngines);
        Assert.Equal(Engine.Cloud, s.AnswerFallback);
        Assert.Equal(AnswerPresets.Default, AnswerPresets.Current(s));
    }

    [Fact]
    public void Cambiar_un_tipo_o_elegir_Personalizado_a_mano_es_Personalizado()
    {
        var s = new AppSettings();
        s.AnswerEngines[AnswerTopics.Math] = Engine.Local;
        Assert.Equal(AnswerPresets.Custom, AnswerPresets.Current(s));

        var chosen = new AppSettings();
        AnswerPresets.Apply(chosen, AnswerPresets.Custom);
        Assert.Equal(AnswerPresets.Custom, AnswerPresets.Current(chosen));
        Assert.Empty(chosen.AnswerEngines); // lo elegido tipo por tipo no se toca
    }
}
