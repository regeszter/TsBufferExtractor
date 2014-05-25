using System;
using TvControl;
using TvDatabase;
using TvEngine.Interfaces;
using TvLibrary.Interfaces;
using TvLibrary.Log;
using TvEngine.Events;
using TvService;

namespace TvEngine
{
  public class TsBufferExtractor : ITvServerPlugin
  {
    static public TvService.TVController Controller;

    #region Constructor

    /// <summary>
    /// Creates a new TsBufferExtractor plugin
    /// </summary>
    public TsBufferExtractor() { }

    #endregion
    /// Starts the plugin
    /// </summary>
    public void Start(IController controller)
    {
      Controller = controller as TvService.TVController;

      ITvServerEvent events = GlobalServiceProvider.Instance.Get<ITvServerEvent>();
      if (events != null)
      {
        events.OnTvServerEvent += new TvServerEventHandler(events_OnTvServerEvent);
      }
    }

    /// <summary>
    /// Stops the plugin
    /// </summary>
    public void Stop()
    {
      ITvServerEvent events = GlobalServiceProvider.Instance.Get<ITvServerEvent>();
      events.OnTvServerEvent -= new TvServerEventHandler(events_OnTvServerEvent);
    }

    public string Author
    {
      get { return "regeszter"; }
    }

    /// <summary>
    /// Should this plugin run only on a master tvserver?
    /// </summary>
    public bool MasterOnly
    {
      get { return true; }
    }

    /// <summary>
    /// Name of this plugin
    /// </summary>
    public string Name
    {
      get { return "TsBufferExtractor"; }
    }

    /// <summary>
    /// Plugin version
    /// </summary>
    public string Version
    {
      get { return  "0.3.0.0"; }
    }

    public SetupTv.SectionSettings Setup
    {
      get { return new SetupTv.Sections.TsBufferExtractorSetup(); }
    }

    /// <summary>
    /// Handles the OnTvServerEvent event fired by the server.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="eventArgs">The <see cref="System.EventArgs"/> the event data.</param>
    void events_OnTvServerEvent(object sender, EventArgs eventArgs)
    {
      TvServerEventArgs tvEvent = (TvServerEventArgs)eventArgs;

      if (eventArgs == null || tvEvent == null)
      {
        return;
      }

      if (tvEvent.EventType == TvServerEventType.StartTimeShifting)
      {
        try
        {
          TvTimeShiftPositionWatcher setNewChannel = new TvTimeShiftPositionWatcher(tvEvent);
          setNewChannel.SetNewChannel(tvEvent.Card.IdChannel);
        }
        catch (Exception ex)
        {
          Log.Error("TsBufferExtractor exception : {0}", ex);
        }
      }
    }
  }
}
