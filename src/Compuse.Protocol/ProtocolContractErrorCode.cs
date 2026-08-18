namespace Compuse.Protocol;

public enum ProtocolContractErrorCode
{
    UnsupportedOutcome = 1,
    InvalidCorrelationId = 2,
    OutcomeDetailMismatch = 3,
    InvalidRefusal = 4,
    InvalidFailure = 5,
    InvalidEvidence = 6,
    InvalidTimestamp = 7,
    MissingCommitmentEvidence = 8,
    UnsupportedTransferEffect = 9,
    InvalidSource = 10,
    InvalidTarget = 11,
    InvalidDeadline = 12
}
