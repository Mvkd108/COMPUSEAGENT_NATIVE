using Compuse.Contracts;
using Google.Protobuf;
using DomainOutcome = Compuse.Contracts.OperationOutcome;
using ProtoEnvelope = Compuse.Protocol.V1.OperationResultEnvelope;
using ProtoOutcome = Compuse.Protocol.V1.OperationOutcome;

namespace Compuse.Protocol.Tests;

[TestClass]
public sealed class WireCompatibilityTests
{
    private const string CanonicalCorrelation = "abcdef01-2345-6789-abcd-ef0123456789";
    private const string CanonicalHex =
        "0a2461626364656630312d323334352d363738392d616263642d6566303132333435363738391004";
    private const string UnknownVarintFieldHex = "980601";

    [TestMethod]
    public void CanonicalIndeterminateSerializesToExactBytes()
    {
        ProtoEnvelope envelope = new()
        {
            CorrelationId = CanonicalCorrelation,
            Outcome = ProtoOutcome.Indeterminate
        };

        Assert.AreEqual(CanonicalHex, Convert.ToHexString(envelope.ToByteArray()).ToLowerInvariant());
    }

    [TestMethod]
    public void DomainIndeterminateSerializesToCanonicalBytes()
    {
        ProtoEnvelope envelope = OperationResultProtoMapper.ToProto(
            OperationResult.Indeterminate(CorrelationId.Parse(CanonicalCorrelation)));

        Assert.AreEqual(CanonicalHex, Convert.ToHexString(envelope.ToByteArray()).ToLowerInvariant());
    }

    [TestMethod]
    public void CanonicalBytesMapToIndeterminateResult()
    {
        ProtoEnvelope parsed = ProtoEnvelope.Parser.ParseFrom(Convert.FromHexString(CanonicalHex));
        OperationResult result = OperationResultProtoMapper.FromProto(parsed);

        Assert.AreEqual(CanonicalCorrelation, result.CorrelationId.ToString());
        Assert.AreEqual(DomainOutcome.Indeterminate, result.Outcome);
        Assert.IsNull(result.Refusal);
        Assert.IsNull(result.Failure);
        Assert.AreEqual(0, result.Evidence.Count);
    }

    [TestMethod]
    public void UnknownVarintFieldIsAcceptedWhilePreservingKnownValues()
    {
        byte[] withUnknown = Convert.FromHexString(CanonicalHex + UnknownVarintFieldHex);
        ProtoEnvelope parsed = ProtoEnvelope.Parser.ParseFrom(withUnknown);
        OperationResult result = OperationResultProtoMapper.FromProto(parsed);

        Assert.AreEqual(CanonicalCorrelation, parsed.CorrelationId);
        Assert.AreEqual(ProtoOutcome.Indeterminate, parsed.Outcome);
        Assert.AreEqual(ProtoEnvelope.DetailOneofCase.None, parsed.DetailCase);
        Assert.AreEqual(0, parsed.Evidence.Count);
        Assert.AreEqual(CanonicalCorrelation, result.CorrelationId.ToString());
        Assert.AreEqual(DomainOutcome.Indeterminate, result.Outcome);
        Assert.AreEqual(0, result.Evidence.Count);
    }
}
