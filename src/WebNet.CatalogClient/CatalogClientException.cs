namespace WebNet.CatalogClient;

public sealed class CatalogClientException : Exception
{
    public CatalogClientException(Guid requestId, string errorCode, string errorMessage)
        : base(errorMessage)
    {
        this.RequestId = requestId;
        this.ErrorCode = errorCode;
    }

    public Guid RequestId { get; }

    public string ErrorCode { get; }
}