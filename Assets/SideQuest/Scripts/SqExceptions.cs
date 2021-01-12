using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Base exception class for api relevant exceptions thrown out ot the SqAppApi
/// </summary>
public class SqApiException : Exception
{
    public SqApiException(int httpCode) : base("Api Exception")
    {
        HttpCode = httpCode;
    }

    public SqApiException(string message, Exception inner = null) : base(message, inner)
    {
    }

    public SqApiException(int httpCode, string message, Exception inner = null) : base(message, inner)
    {
        HttpCode = httpCode;
    }

    public SqApiException() { }

    /// <summary>
    /// When set, the HTTP status code that was returned which resulted in an exception
    /// </summary>
    public int? HttpCode { get; private set; }
}

/// <summary>
/// Exception thrown when Unity reports that there were network problems
/// </summary>
public class SqApiNetworkException : SqApiException
{
    public SqApiNetworkException() { }
    public SqApiNetworkException(string message, Exception inner = null) : base(message, inner) { }
}

/// <summary>
/// Exception thrown when there are authorization or authentication related issues
/// </summary>
public class SqApiAuthException : SqApiException
{
    public SqApiAuthException(int httpCode) : base(httpCode) { }
    public SqApiAuthException(string message, Exception inner = null) : base(message, inner) { }
    public SqApiAuthException(int httpCode, string message, Exception inner = null) : base(httpCode, message, inner) { }
}

/// <summary>
/// Exception raised when an object being created already exists on the server
/// </summary>
public class SqAlreadyExistsException : SqApiException
{
    public SqAlreadyExistsException(int httpCode) : base(httpCode) { }
    public SqAlreadyExistsException(string message, Exception inner = null) : base(message, inner) { }
    public SqAlreadyExistsException(int httpCode, string message, Exception inner = null) : base(httpCode, message, inner) { }
}