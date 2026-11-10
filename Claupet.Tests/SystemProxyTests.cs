namespace Claupet.Tests;

/// <summary>
/// Las variables del proxy que reciben los agentes y los shells de la Terminal (SystemProxy.Variables), con el proxy,
/// las variables y las excepciones dadas: no se tocan las del proceso de las pruebas.
/// </summary>
public sealed class SystemProxyTests
{
    static Func<string, string?> Env(params (string Name, string Value)[] vars) =>
        name => vars.FirstOrDefault(v => v.Name == name).Value;

    [Fact]
    public void Sin_proxy_no_lleva_nada()
    {
        Assert.Empty(SystemProxy.Variables(null, Env(), [], node: true));
    }

    [Fact]
    public void Con_el_proxy_de_Windows_lleva_las_variables_y_el_equipo_va_directo()
    {
        var vars = SystemProxy.Variables("http://192.168.49.1:8228", Env(), ["<local>", "*.empresa.local", "10.*"], node: false);
        Assert.Equal("http://192.168.49.1:8228", vars["HTTPS_PROXY"]);
        Assert.Equal("http://192.168.49.1:8228", vars["HTTP_PROXY"]);
        Assert.Equal("localhost,127.0.0.1,::1,.empresa.local,10.*", vars["NO_PROXY"]);
        Assert.False(vars.ContainsKey("NODE_USE_ENV_PROXY"));
    }

    [Fact]
    public void Node_lo_mira_solo_con_su_variable()
    {
        var vars = SystemProxy.Variables("http://proxy:8080", Env(), [], node: true);
        Assert.Equal("1", vars["NODE_USE_ENV_PROXY"]);
    }

    [Fact]
    public void No_pisa_lo_que_ya_trae_la_app()
    {
        var vars = SystemProxy.Variables("http://proxy:8080", Env(("https_proxy", "http://otro:1"), ("NO_PROXY", "interno")), [], node: false);
        Assert.False(vars.ContainsKey("HTTPS_PROXY"));
        Assert.Equal("http://proxy:8080", vars["HTTP_PROXY"]);
        Assert.False(vars.ContainsKey("NO_PROXY"));
    }
}
