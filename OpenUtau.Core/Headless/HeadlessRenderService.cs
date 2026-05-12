using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenUtau.Core.Headless;

public class HeadlessRenderService {
    public Task RenderMixdownAsync(
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrEmpty(inputPath);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);

        if (!inputPath.EndsWith(".ustx", StringComparison.OrdinalIgnoreCase)) {
            throw new ArgumentException("Input path must end with .ustx.", nameof(inputPath));
        }

        if (!outputPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) {
            throw new ArgumentException("Output path must end with .wav.", nameof(outputPath));
        }

        cancellationToken.ThrowIfCancellationRequested();

        throw new NotImplementedException("Headless rendering is not wired yet.");
    }
}
