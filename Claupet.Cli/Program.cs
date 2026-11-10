using System.Text;
using Claupet;

// cpt: todo vive en Core/Contratos/CptCli.cs, para que las pruebas lo usen sin este ejecutable.
Console.OutputEncoding = new UTF8Encoding(false);
return CptCli.Run(args, Console.Out, Console.Error, Environment.CurrentDirectory);
