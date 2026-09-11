using Compuse.Discovery;
using Compuse.Requests;

namespace Compuse.Routing;

public sealed class ApplicationImportPlan
{
    public const string ApplicationBackendId = "application_surface";
    public const string DocumentTextVerification = "document_text";

    public ApplicationImportPlan(
        TransferEffect effect,
        WindowIdentity target,
        string sourcePath,
        long byteLength,
        string expectedText,
        FilesystemIdentity sourceIdentity,
        string recipeName)
    {
        if (effect == default || !Enum.IsDefined(effect))
        {
            throw new ArgumentOutOfRangeException(nameof(effect), effect, "Transfer effect must be a defined nonzero value.");
        }

        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(expectedText);
        ArgumentNullException.ThrowIfNull(sourceIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipeName);
        if (byteLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength), byteLength, "Byte length cannot be negative.");
        }

        Effect = effect;
        Target = target;
        SourcePath = sourcePath;
        ByteLength = byteLength;
        ExpectedText = expectedText;
        SourceIdentity = sourceIdentity;
        RecipeName = recipeName;
        BackendId = ApplicationBackendId;
        VerificationStrategy = DocumentTextVerification;
    }

    public string BackendId { get; }

    public TransferEffect Effect { get; }

    public string VerificationStrategy { get; }

    public WindowIdentity Target { get; }

    public string SourcePath { get; }

    public long ByteLength { get; }

    public string ExpectedText { get; }

    public FilesystemIdentity SourceIdentity { get; }

    public string RecipeName { get; }
}
