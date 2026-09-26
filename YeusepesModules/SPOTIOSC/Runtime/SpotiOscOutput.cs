namespace YeusepesModules.SPOTIOSC.Runtime;

internal sealed class SpotiOscOutput
{
    private readonly Action<SpotiOSC.SpotiParameters, object> _setParameter;
    private readonly Action<string, bool> _sendAddress;
    private readonly Action<string> _triggerEvent;
    private readonly Action<string> _changeState;
    private readonly Action<string> _logDebug;
    private readonly Dictionary<SpotiOSC.SpotiParameters, int> _pulseVersions = [];

    public SpotiOscOutput(
        Action<SpotiOSC.SpotiParameters, object> setParameter,
        Action<string, bool> sendAddress,
        Action<string> triggerEvent,
        Action<string> changeState,
        Action<string> logDebug)
    {
        _setParameter = setParameter;
        _sendAddress = sendAddress;
        _triggerEvent = triggerEvent;
        _changeState = changeState;
        _logDebug = logDebug;
    }

    public void Set(SpotiOSC.SpotiParameters parameter, object value) =>
        _setParameter(parameter, value);

    public void Send(string address, bool value)
    {
        try
        {
            _sendAddress(address, value);
            _logDebug($"Sent {address} = {value}");
        }
        catch (Exception exception)
        {
            _logDebug($"Failed to send {address}: {exception.Message}");
        }
    }

    public void Trigger(string eventName) => _triggerEvent(eventName);

    public void ChangeState(string stateName) => _changeState(stateName);

    public Task PulseErrorAsync() => PulseAsync(SpotiOSC.SpotiParameters.Error);

    public async Task PulseAsync(SpotiOSC.SpotiParameters parameter)
    {
        int version;
        lock (_pulseVersions)
        {
            _pulseVersions[parameter] = version = _pulseVersions.GetValueOrDefault(parameter) + 1;
            Set(parameter, false);
            Set(parameter, true);
        }
        await Task.Delay(100);
        lock (_pulseVersions)
        {
            if (_pulseVersions[parameter] == version) Set(parameter, false);
        }
    }
}
