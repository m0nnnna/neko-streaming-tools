namespace NekoStreamer.Core.Pipeline;

public enum PipelineState
{
    Stopped,
    WaitingForSource,
    Live,
    Faulted,
}
