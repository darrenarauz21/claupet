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
| Avalonia (Linux y macOS) | 11.3.22 | MIT | https://github.com/AvaloniaUI/Avalonia |
| Microsoft.ICU.ICU4C.Runtime (Linux) | 72.1.0.3 | Unicode License | https://www.unicode.org/copyright.html |

El texto completo de cada licencia viene en su paquete NuGet.

## Modelos que se descargan al usarlos

La app no incluye estos modelos: los baja de Hugging Face la primera vez que se activa la función.

| Modelo | Uso | Licencia | Fuente |
|---|---|---|---|
| Whisper `ggml-small.bin` | Micrófono (voz a texto) | MIT | https://huggingface.co/ggerganov/whisper.cpp |
| Supertonic 3 | Voz de Claupet (texto a voz) | BigScience Open RAIL-M | https://huggingface.co/supertone-oss-archive/supertonic-3 |

Open RAIL-M prohíbe ciertos usos del modelo (los de su Anexo A); quien use la voz de Claupet acepta
esas condiciones.

## Marcas

Claupet es un proyecto personal y no oficial, sin relación con Anthropic. «Claude» y «Claude Code»
son marcas de Anthropic, PBC.
