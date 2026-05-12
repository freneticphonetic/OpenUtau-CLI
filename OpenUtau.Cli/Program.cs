namespace OpenUtau.Cli;

internal static class Program {
    private const int ExitSuccess = 0;
    private const int ExitInvalidArguments = 1;
    private const int ExitRenderNotImplemented = 2;

    private static int Main(string[] args) {
        if (args.Length == 1 && args[0] == "--help") {
            PrintUsage(Console.Out);
            return ExitSuccess;
        }

        if (args.Length == 0) {
            return Invalid("Missing command.");
        }

        if (args[0] != "render") {
            return Invalid($"Unknown command '{args[0]}'.");
        }

        return RunRender(args);
    }

    private static int RunRender(string[] args) {
        if (args.Length < 2) {
            return Invalid("Missing input .ustx path.");
        }

        var inputPath = args[1];
        if (!inputPath.EndsWith(".ustx", StringComparison.OrdinalIgnoreCase)) {
            return Invalid("Input path must end with .ustx.");
        }

        if (args.Length < 3) {
            return Invalid("Missing --out option.");
        }

        if (args[2] != "--out") {
            return Invalid("Expected --out option after input path.");
        }

        if (args.Length < 4) {
            return Invalid("Missing output .wav path.");
        }

        var outputPath = args[3];
        if (!outputPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) {
            return Invalid("Output path must end with .wav.");
        }

        if (args.Length > 4) {
            return Invalid("Unexpected extra arguments.");
        }

        Console.Error.WriteLine("Render command recognized, but rendering is not wired yet.");
        return ExitRenderNotImplemented;
    }

    private static int Invalid(string message) {
        Console.Error.WriteLine($"Error: {message}");
        PrintUsage(Console.Error);
        return ExitInvalidArguments;
    }

    private static void PrintUsage(TextWriter writer) {
        writer.WriteLine("Usage:");
        writer.WriteLine("  openutau-cli --help");
        writer.WriteLine("  openutau-cli render <input.ustx> --out <output.wav>");
    }
}
