using System.Threading.Channels;

namespace FitEdu.Infrastructure.ApiServices;

/// <summary>One email waiting to be sent: the recipient and the finished subject and bodies.</summary>
public sealed record QueuedEmail(string To, EmailContent Content);

/// <summary>
/// The in-memory line of emails between the code that asks for one (sign-up, forgotten password) and
/// <see cref="EmailDispatcher"/>, which sends them. A request therefore never waits for the SMTP relay.
/// The line is bounded, so a dead relay cannot grow memory without limit; a message still waiting when
/// the app stops is lost, and the member asks for a new link.
/// </summary>
public sealed class EmailOutbox
{
    /// <summary>Messages the line holds before it refuses new ones.</summary>
    public const int DefaultCapacity = 500;

    private readonly Channel<QueuedEmail> _channel;

    public EmailOutbox(int capacity = DefaultCapacity)
    {
        _channel = Channel.CreateBounded<QueuedEmail>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    /// <summary>The side the dispatcher reads from.</summary>
    public ChannelReader<QueuedEmail> Reader => _channel.Reader;

    /// <summary>Adds an email without waiting; false when the line is full.</summary>
    public bool TryEnqueue(QueuedEmail email) => _channel.Writer.TryWrite(email);
}
