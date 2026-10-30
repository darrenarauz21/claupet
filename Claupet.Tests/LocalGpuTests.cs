namespace Claupet.Tests;

/// <summary>La tarjeta gráfica del modelo local, como la nombra llama-server --list-devices (versión b11913, Vulkan).</summary>
public sealed class LocalGpuTests
{
    [Fact]
    public void Una_integrada_dice_que_lo_es()
    {
        const string text = """
            ggml_vulkan: Found 1 Vulkan devices:
            ggml_vulkan: 0 = AMD Radeon(TM) Graphics (AMD proprietary driver) | uma: 1 | fp16: 1 | bf16: 0 | fp4: 0 | warp size: 64 | shared memory: 32768 | int dot: 0 | matrix cores: none
            Available devices:
              Vulkan0: AMD Radeon(TM) Graphics (12978 MiB, 12329 MiB free)
            """;
        Assert.Equal("AMD Radeon(TM) Graphics (integrada)", LocalModel.ParseGpu(text));
    }

    [Fact]
    public void Una_dedicada_va_con_su_nombre()
    {
        const string text = "ggml_vulkan: 0 = NVIDIA GeForce RTX 4060 (NVIDIA) | uma: 0 | fp16: 1 | warp size: 32\r\nAvailable devices:\r\n";
        Assert.Equal("NVIDIA GeForce RTX 4060", LocalModel.ParseGpu(text));
    }

    [Fact]
    public void Capturada_solo_viene_la_lista()
    {
        // Lo que lee la app (sin terminal): sin la línea de ggml_vulkan.
        const string text = "Available devices:\r\n  Vulkan0: AMD Radeon(TM) Graphics (12978 MiB, 12329 MiB free)\r\n";
        Assert.Equal("AMD Radeon(TM) Graphics", LocalModel.ParseGpu(text));
    }

    [Fact]
    public void Sin_gráfica_no_hay_nombre() =>
        Assert.Null(LocalModel.ParseGpu("Available devices:\n"));
}
