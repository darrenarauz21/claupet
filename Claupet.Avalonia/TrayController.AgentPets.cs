using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Claupet;

/// <summary>
/// Una mascota por cada otro agente que se sigue (Ajustes › Apariencia › Mascota › «Una mascota por cada agente»): junto a
/// la de Claude Code, cada agente con su personaje (Agents.PetOf: Ciel para Codex y Gemma para Gemini si no se eligió
/// otro). Como de esos agentes solo se sabe cuándo escribieron en sus sesiones (Agents.LastWrite), cada una teclea
/// mientras su agente trabaja, queda quieta si trabajó hace poco y si no, duerme. Se mueven y se guardan aparte; con un
/// clic reaccionan como la otra, y «Ocultar del escritorio» le saca la mascota a ese agente.
/// </summary>
sealed partial class TrayController
{
    sealed record AgentPet(string Kind, ChibiSkin Skin, MascotWindow Window, ChibiDirector Director);

    readonly Dictionary<string, AgentPet> _agentPets = new();
    DispatcherTimer? _agentPetTimer;

    /// <summary>Lo que se pide: con la mascota del escritorio y el interruptor, cada agente que se sigue, está en el equipo y lleva mascota.</summary>
    IEnumerable<(AgentAccount Agent, ChibiSkin Skin)> WantedAgentPets()
    {
        if (!_settings.DesktopMascot || !_settings.AgentPets) yield break;
        foreach (var agent in _settings.Agents.Where(a => a.Follow))
            if (_agents.Any(s => s.Account == agent && s.Exists) && ChibiSprite.SkinOf(Agents.PetOf(_settings, agent.Kind)) is { } skin)
                yield return (agent, skin);
    }

    /// <summary>Abre las que faltan, cierra las que sobran y rehace las que cambiaron de personaje o de comportamiento.</summary>
    void UpdateAgentPets()
    {
        var wanted = WantedAgentPets().ToDictionary(w => w.Agent.Kind);
        foreach (var (kind, pet) in _agentPets.ToList())
            if (!wanted.TryGetValue(kind, out var w) || w.Skin != pet.Skin || pet.Window.AllDesktops != _settings.MascotAllDesktops)
            {
                pet.Window.Close();
                _agentPets.Remove(kind);
            }
        var i = 0;
        foreach (var (agent, skin) in wanted.Values)
        {
            i++;
            if (_agentPets.ContainsKey(agent.Kind)) continue;
            var director = new ChibiDirector { Settings = _settings, Skin = skin };
            var window = new MascotWindow(director, SpotFor(agent.Kind, i), skin, () => _theme.Accent, _settings.MascotAllDesktops, _settings.MascotSize);
            ToolTip.SetTip(window, agent.Label);
            var kind = agent.Kind;
            window.Poked += director.Poke;
            window.OpenRequested += ShowForm;
            window.HideRequested += () =>
            {
                _settings.AgentPet[kind] = ""; // ese agente queda sin mascota (se vuelve a elegir en Ajustes)
                SettingsStore.Save(_settings);
                UpdateAgentPets();
                PushSettings();
            };
            window.Moved += at =>
            {
                _settings.AgentPetSpots[kind] = new PetSpot(at.X, at.Y);
                SettingsStore.Save(_settings);
            };
            _agentPets[kind] = new AgentPet(kind, skin, window, director);
            window.Show();
        }
        FeedAgentPets();
        if (_agentPets.Count > 0 && _agentPetTimer is null)
        {
            // Cada 15 s mira cuándo escribió cada agente: solo la fecha de sus archivos, sin leerlos.
            _agentPetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _agentPetTimer.Tick += (_, _) => FeedAgentPets();
            _agentPetTimer.Start();
        }
        else if (_agentPets.Count == 0)
        {
            _agentPetTimer?.Stop();
            _agentPetTimer = null;
        }
    }

    /// <summary>Dónde aparece: donde se dejó o, la primera vez, a la izquierda de la de Claude Code, una al lado de la otra.</summary>
    PixelPoint? SpotFor(string kind, int order)
    {
        if (_settings.AgentPetSpots.TryGetValue(kind, out var spot)) return new PixelPoint(spot.X, spot.Y);
        if (_mascotWindow is not { } main) return null;
        var bounds = main.ScreenBounds;
        return new PixelPoint(bounds.X - order * (bounds.Width + bounds.Width / 6), bounds.Y);
    }

    /// <summary>Lo que hace cada una según cuándo escribió su agente (fuera del hilo de la interfaz: mira archivos).</summary>
    async void FeedAgentPets()
    {
        if (_agentPets.Count == 0) return;
        var pets = _agentPets.Values.ToList();
        var agents = _settings.Agents.ToList();
        var last = await Task.Run(() => pets.ToDictionary(p => p.Kind, p => agents.FirstOrDefault(a => a.Kind == p.Kind) is { } a ? Agents.LastWrite(a) : null));
        var now = DateTimeOffset.UtcNow;
        foreach (var pet in pets)
            if (_agentPets.ContainsValue(pet)) pet.Director.SetActivity(last.GetValueOrDefault(pet.Kind), now);
    }

    void CloseAgentPets()
    {
        _agentPetTimer?.Stop();
        _agentPetTimer = null;
        foreach (var pet in _agentPets.Values) pet.Window.Close();
        _agentPets.Clear();
    }

    /// <summary>Elegir el personaje de un agente (Claupet Console): cualquiera, o ninguno.</summary>
    void ChooseAgentPet(string kind) =>
        Choose([("", L.T("Ninguna")), .. ChibiSprite.Skins.Select(s => (s.Id, s.Name))], Agents.PetOf(_settings, kind), id =>
        {
            _settings.AgentPet[kind] = id;
            SettingsStore.Save(_settings);
            UpdateAgentPets();
            PushSettings();
        });
}
