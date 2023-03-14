namespace EmailForwarder;

public class AppSettings
{
    public List<EmailForwardingRule> ForwardingRules { get; set; }
    public ReceiverSettings ReceiverSettings { get; set; }
    public SenderSettings SenderSettings { get; set; }
    public int DelayMilliseconds { get; set; }
}