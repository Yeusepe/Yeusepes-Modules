using System.Text.Json;
using YeusepesModules.SPOTIOSC.Utils.Requests;

namespace YeusepesModules.SPOTIOSC.Runtime.Jam;

internal sealed class JamEventHandler
{
    private readonly SpotifyRequestContext _context;
    private readonly SpotifyJamService _jam;
    private readonly SpotiOscOutput _output;
    private readonly Action<string> _logDebug;
    private string? _shareUri;

    public JamEventHandler(
        SpotifyRequestContext context,
        SpotifyJamService jam,
        SpotiOscOutput output,
        Action<string> logDebug)
    {
        _context = context;
        _jam = jam;
        _output = output;
        _logDebug = logDebug;
    }

    public void Handle(JsonElement payload)
    {
        if (payload.TryGetProperty("session", out var session)) ApplySession(session);
        if (payload.TryGetProperty("reason", out var reason) && reason.GetString() == "SESSION_DELETED")
        {
            _shareUri = null;
            _jam.ApplyLeave();
        }
    }

    private void ApplySession(JsonElement session)
    {
        try
        {
            var state = _jam.State;
            if (session.TryGetProperty("session_id", out var id)) state.SessionId = id.GetString();
            if (session.TryGetProperty("join_session_token", out var token)) state.JoinToken = token.GetString();
            if (session.TryGetProperty("is_listening", out var listening))
            {
                state.IsListening = listening.GetBoolean();
                _output.Set(SpotiOSC.SpotiParameters.SessionIsListening, state.IsListening);
            }
            if (session.TryGetProperty("is_controlling", out var controlling))
            {
                state.IsControlling = controlling.GetBoolean();
                _output.Set(SpotiOSC.SpotiParameters.SessionIsControlling, state.IsControlling);
            }
            if (session.TryGetProperty("queue_only_mode", out var queueOnly))
            {
                state.QueueOnlyMode = queueOnly.GetBoolean();
                _output.Set(SpotiOSC.SpotiParameters.QueueOnlyMode, state.QueueOnlyMode);
            }
            if (session.TryGetProperty("maxMemberCount", out var maxMembers))
            {
                state.MaxMemberCount = maxMembers.GetInt32();
                _output.Set(SpotiOSC.SpotiParameters.SessionMaxMemberCount, state.MaxMemberCount);
            }
            if (session.TryGetProperty("active", out var active))
            {
                _jam.SetActive(active.GetBoolean());
                _output.Trigger("JamEvent");
            }
            if (session.TryGetProperty("host_device_info", out var host) &&
                host.TryGetProperty("is_group", out var isGroup))
            {
                state.HostIsGroup = isGroup.GetBoolean();
                _output.Set(SpotiOSC.SpotiParameters.HostIsGroup, state.HostIsGroup);
            }

            ApplyMembers(session);
            if (session.TryGetProperty("join_session_uri", out var uri))
                _ = UpdateShareableCodeAsync(uri.GetString());
        }
        catch (Exception exception)
        {
            _logDebug($"Jam session update failed: {exception.Message}");
        }
    }

    private void ApplyMembers(JsonElement session)
    {
        if (!session.TryGetProperty("session_members", out var members) ||
            members.ValueKind != JsonValueKind.Array)
            return;

        var ownerId = session.TryGetProperty("session_owner_id", out var owner) ? owner.GetString() : null;
        var images = new List<string>();
        var isOwner = false;
        foreach (var member in members.EnumerateArray())
        {
            if (member.ValueKind != JsonValueKind.Object) continue;
            var memberId = member.TryGetProperty("id", out var id) ? id.GetString() : null;
            if (member.TryGetProperty("is_current_user", out var currentUser) && currentUser.GetBoolean())
                isOwner = memberId == ownerId;
            if (memberId == ownerId)
                _context.JamOwnerName = member.TryGetProperty("display_name", out var name) ? name.GetString() : null;

            var image = member.TryGetProperty("image_url", out var normalImage) ? normalImage.GetString() : null;
            if (string.IsNullOrEmpty(image) && member.TryGetProperty("large_image_url", out var largeImage))
                image = largeImage.GetString();
            if (!string.IsNullOrEmpty(image)) images.Add(image);
        }

        _jam.State.ParticipantCount = members.GetArrayLength();
        _context.JamParticipantImages = images;
        _context.IsJamOwner = isOwner;
        _output.Set(SpotiOSC.SpotiParameters.JamParticipantCount, _jam.State.ParticipantCount);
        _output.Set(SpotiOSC.SpotiParameters.IsJamOwner, isOwner);
    }

    private async Task UpdateShareableCodeAsync(string? uri)
    {
        if (string.IsNullOrEmpty(uri) || _shareUri == uri) return;
        _shareUri = uri;
        try
        {
            var code = await _jam.GenerateShareableCodeAsync(uri);
            if (_shareUri != uri) return;
            _jam.State.ShareableUrl = string.IsNullOrEmpty(code) ? null : $"https://spotify.link/{code}";
            _context.JamShortCode = code;
        }
        catch (Exception exception)
        {
            _logDebug($"Jam share URL generation failed: {exception.Message}");
        }
        finally
        {
            if (_shareUri == uri) _shareUri = null;
        }
    }

}
