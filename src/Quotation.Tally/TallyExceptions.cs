namespace Quotation.Tally;

/// <summary>Tally answered, but reported an error (e.g. company not loaded, bad request).</summary>
public class TallyException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Tally could not be reached (not running, XML port disabled, network down, timeout).</summary>
public sealed class TallyUnavailableException(string message, Exception? inner = null) : TallyException(message, inner);
