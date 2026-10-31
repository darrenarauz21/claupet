namespace Claupet.Tests;

/// <summary>La tarjeta gráfica del modelo local, como la nombra llama-server -v --list-devices (versión b11913, Vulkan).</summary>
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
    public void Con_integrada_y_dedicada_va_la_dedicada_aunque_sea_la_segunda()
    {
        // Lo que escribe -v --list-devices en una laptop con Intel y NVIDIA: llama.cpp corre en la NVIDIA (Vulkan1).
        const string text = """
            0.00.180.629 D ggml_vulkan: Found 2 Vulkan devices:
            0.00.184.990 D ggml_vulkan: 0 = Intel(R) UHD Graphics 770 (Intel Corporation) | uma: 1 | fp16: 1 | bf16: 0 | fp4: 0 | warp size: 32 | shared memory: 32768 | int dot: 1 | matrix cores: none
            0.00.188.468 D ggml_vulkan: 1 = NVIDIA GeForce RTX 4050 Laptop GPU (NVIDIA) | uma: 0 | fp16: 1 | bf16: 1 | fp4: 0 | warp size: 32 | shared memory: 49152 | int dot: 1 | matrix cores: NV_coopmat2
            0.00.188.512 I load_backend: loaded Vulkan backend from C:\modelo-local\llama-b11913-vulkan\ggml-vulkan.dll
            Available devices:
              Vulkan0: Intel(R) UHD Graphics 770 (16245 MiB, 15477 MiB free)
              Vulkan1: NVIDIA GeForce RTX 4050 Laptop GPU (5920 MiB, 5152 MiB free)
            """;
        Assert.Equal("NVIDIA GeForce RTX 4050 Laptop GPU", LocalModel.ParseGpu(text));
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
