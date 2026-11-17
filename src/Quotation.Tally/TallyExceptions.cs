namespace Quotation.Tally;

/// <summary>Tally answered, but reported an error (e.g. company not loaded, bad request).</summary>
public class TallyException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Tally could not be reached (not running, XML port disabled, network down, timeout).</summary>
public class TallyUnavailableException(string message, Exception? inner = null) : TallyException(message, inner);

/// <summary>Tally did not answer in time (it is busy or the request was too heavy).</summary>
public sealed class TallyTimeoutException(string message, Exception? inner = null) : TallyUnavailableException(message, inner);

/// <summary>Tally answered something that is not readable XML (or nothing at all).</summary>
public sealed class TallyUnreadableException(string message, Exception? inner = null) : TallyException(message, inner);

/// <summary>Tally answered with an HTTP error status.</summary>
public sealed class TallyHttpException(string message, Exception? inner = null) : TallyException(message, inner);
