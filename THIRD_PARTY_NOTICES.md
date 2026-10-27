# Componentes de terceros

Claupet se distribuye bajo la licencia MIT (ver [LICENSE](LICENSE)). Los paquetes de un solo archivo
incluyen las bibliotecas de esta lista, cada una con su propia licencia.

## Bibliotecas incluidas

| Componente | Versión | Licencia | Fuente |
|---|---|---|---|
| Whisper.net | 1.9.1 | MIT, © 2024 sandrohanea | https://github.com/sandrohanea/whisper.net |
| Whisper.net.Runtime (whisper.cpp) | 1.9.1 | MIT | https://github.com/ggml-org/whisper.cpp |
| Microsoft.ML.OnnxRuntime | 1.20.1 | MIT, © Microsoft Corporation | https://github.com/microsoft/onnxruntime |
| System.Speech | 8.0.0 | MIT, © .NET Foundation | https://github.com/dotnet/runtime |
| Avalonia (Claupet Console; la app de Linux y macOS) | 11.3.22 | MIT | https://github.com/AvaloniaUI/Avalonia |
| SkiaSharp (con Skia) | 2.88.9 | MIT, © Microsoft Corporation (Skia: BSD-3-Clause) | https://github.com/mono/SkiaSharp |
| HarfBuzzSharp (con HarfBuzz) | 8.3.1.1 | MIT, © Microsoft Corporation (HarfBuzz: Old MIT) | https://github.com/mono/SkiaSharp |
| ANGLE (Avalonia.Angle.Windows.Natives) | 2.1.25547.20250602 | BSD-3-Clause, © The ANGLE Project Authors | https://chromium.googlesource.com/angle/angle |
| Microsoft.ICU.ICU4C.Runtime (Linux) | 72.1.0.3 | Unicode License | https://www.unicode.org/copyright.html |

El texto completo de cada licencia viene en su paquete NuGet.

## Modelos que se descargan al usarlos

La app no incluye estos modelos ni programas: los baja (de Hugging Face y de GitHub) la primera vez que
se activa la función.

| Modelo | Uso | Licencia | Fuente |
|---|---|---|---|
| Whisper `ggml-small.bin` | Micrófono (voz a texto) | MIT | https://huggingface.co/ggerganov/whisper.cpp |
| Supertonic 3 | Voz de Claupet (texto a voz) | BigScience Open RAIL-M | https://huggingface.co/supertone-oss-archive/supertonic-3 |
| Qwen3.5 0.8B, 2B o 4B (GGUF Q4_K_M de Unsloth) | Modelo local de la mascota y la isla | Apache 2.0 | https://huggingface.co/Qwen |
| llama.cpp `llama-server` b11913 (CPU x64) | Corre el modelo local | MIT; incluye LLVM OpenMP (Apache 2.0 con excepción LLVM) | https://github.com/ggml-org/llama.cpp |

Open RAIL-M prohíbe ciertos usos del modelo (los de su Anexo A); quien use la voz de Claupet acepta
esas condiciones.

## Servicios en línea

Sin IA, la app solo usa internet para el clima de Ajustes › Respuestas:

| Servicio | Para qué | Condiciones |
|---|---|---|
| [Open-Meteo](https://open-meteo.com) | Pronóstico y búsqueda de ciudades | Uso no comercial sin clave; datos bajo CC BY 4.0 |
| [ipwho.is](https://ipwho.is) | La ciudad por la IP, si «Mi ciudad» es automática | Uso no comercial sin clave |

## Marcas

Claupet es un proyecto personal y no oficial, sin relación con Anthropic. «Claude» y «Claude Code»
son marcas de Anthropic, PBC.
