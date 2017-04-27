using System;
using System.Threading;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Http;
using TvControl;
using TvLibrary.Interfaces;
using TvLibrary.Log;
using TvEngine.Events;
using TvEngine;
using TsBufferExtractor.Interface;

namespace TsBufferExtractor
{
  public class TsBufferExtractor : ITvServerPlugin
  {
    static public TvService.TVController Controller;
    HttpChannel httpChannel;

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

      try
      {
        httpChannel = new HttpChannel(9998);
        ChannelServices.RegisterChannel(httpChannel, false);
      }
      catch (Exception ex)
      {
        //Log.Error("TsBufferExtractor exception: {0}", ex);
      }

      try
      {
        RemotingConfiguration.RegisterWellKnownServiceType(typeof(TsBufferExtractorServer), "TsBufferExtractorServer",
         WellKnownObjectMode.SingleCall);
      }
      catch (Exception ex)
      {
        Log.Error("TsBufferExtractor exception: {0}", ex);
      }
      Log.Debug("TsBufferExtractor Started");
    }

    /// <summary>
    /// Stops the plugin
    /// </summary>
    public void Stop()
    {
      ITvServerEvent events = GlobalServiceProvider.Instance.Get<ITvServerEvent>();
      events.OnTvServerEvent -= new TvServerEventHandler(events_OnTvServerEvent);
      try
      {
        ChannelServices.UnregisterChannel(httpChannel);
      }
      catch (Exception ex)
      {
        //Log.Error("TsBufferExtractor exception: {0}", ex);
      }
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
      get { return  "0.6.1.0"; }
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
          Thread doWork = new Thread(delegate()
            {
              new TvTimeShiftPositionWatcher(tvEvent);
            });
          doWork.Start();
        }
        catch (Exception ex)
        {
          Log.Error("TsBufferExtractor exception : {0}", ex);
        }
      }
    }
  }
}
