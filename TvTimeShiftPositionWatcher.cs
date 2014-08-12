using System;
using System.Collections.Generic;
using System.Collections;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.IO;
using TvControl;
using TvDatabase;
using TvEngine.Interfaces;
using TvLibrary.Interfaces;
using TvLibrary.Log;
using TvEngine.Events;
using System.Timers;


namespace TsBufferExtractor
{
  public class TvTimeShiftPositionWatcher
  {
    #region Variables

    private long _snapshotBufferId = 0;
    private long _bufferId = 0;
    private System.Timers.Timer _timer = null;
    private int _idChannelToWatch = -1;
    private Int64 _snapshotBufferPosition = -1;
    private string _snapshotBufferFile = string.Empty;
    private decimal _preRecordInterval = -1;
    private int _secondsElapsed = 0;
    private TvServerEventArgs _tvEvent;
    private string _tsBufferExtractorSetup;
    private static string _isManual = string.Empty; //empty if no client
    int _errors = 0;
    
    #endregion

    public TvTimeShiftPositionWatcher(TvServerEventArgs eventArgs)
    {
      _tvEvent = eventArgs;
    }

    #region Event handlers

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
      if ((tvEvent.EventType == TvServerEventType.EndTimeShifting || tvEvent.EventType == TvServerEventType.StartZapChannel)
        && _idChannelToWatch == tvEvent.Card.IdChannel && _tvEvent.User.Name == tvEvent.User.Name)
      {
        StopTimer();
      }

      if (tvEvent.EventType == TvServerEventType.RecordingStarted && _idChannelToWatch == tvEvent.Card.IdChannel)
      {
        int i = 0;
        while (i < 50)
        {
          Log.Debug("TsBufferExtractor: waiting for the signal.");
          if (_isManual == _tvEvent.User.Name)
          {
            break;
          }

          Thread.Sleep(100);
          i++;
        }

        if (_isManual == _tvEvent.User.Name)
        {
          _isManual = string.Empty;
          CheckRecordingStatus();
          SnapshotTimeShiftBuffer();
        }
        else
        {
          Log.Debug("TsBufferExtractor: it is not a manual recording or timeout.");
        }
      }
    }

    #endregion

    #region Public methods

    public static string IsManual
    {
      set {_isManual = value;}
    }

    public void SetNewChannel(int idChannel)
    {

      TvBusinessLayer layer = new TvBusinessLayer();
      _tsBufferExtractorSetup = layer.GetSetting("TsBufferExtractorSetup", "A").Value;
      _preRecordInterval = Decimal.Parse(layer.GetSetting("preRecordInterval", "5").Value);
      _snapshotBufferPosition = -1;
      _snapshotBufferFile = string.Empty;
      _snapshotBufferId = 0;

      Log.Debug("TsBufferExtractor: SetNewChannel({0})", idChannel);
      _idChannelToWatch = idChannel;

      StartTimer();
    }
    #endregion

    #region Private methods

    private void StopTimer()
    {
      _timer.Stop();
      _timer.Dispose();
      ITvServerEvent events = GlobalServiceProvider.Instance.Get<ITvServerEvent>();
      events.OnTvServerEvent -= new TvServerEventHandler(events_OnTvServerEvent);
      Log.Debug("TsBufferExtractor: Timer stopped on {0}, errors = {1}", _tvEvent.User.Name, _errors);
    }

    private void _timer_Tick(object sender, EventArgs e)
    {
      if (_errors >= 10)
      {
        StopTimer();
      }
      
      if (_tsBufferExtractorSetup != "A" && _snapshotBufferPosition == -1)
      {
        SnapshotTimeShiftBuffer();
        Log.Debug("TsBufferExtractor: storing the timeshift position on the channel change.");
      }

      UpdateTimeShiftReusedStatus();
      _secondsElapsed++;

      if (_secondsElapsed == 60)
      {
        _secondsElapsed = 0;
        CheckOrUpdateTimeShiftPosition();
      }
    }

    private void StartTimer()
    {
      if (_timer == null)
      {
        _timer = new System.Timers.Timer();
        _timer.Elapsed += new ElapsedEventHandler(_timer_Tick);
        _timer.AutoReset = true;
        _timer.Interval = 500;
        _timer.Start();
 
        try
        {
          ITvServerEvent events = GlobalServiceProvider.Instance.Get<ITvServerEvent>();
          events.OnTvServerEvent += new TvServerEventHandler(events_OnTvServerEvent);
        }
        catch (Exception ex)
        {
          Log.Error("TsBufferExtractor exception : {0}", ex);
        }
      }
      Log.Debug("TsBufferExtractor: started on {1}, BufferExtractorSetup = {0}", _tsBufferExtractorSetup, _tvEvent.User.Name);
    }

    public void CheckRecordingStatus()
    {
      try
      {
        if (_tvEvent.Card.IsRecording)
        {
          int scheduleId = _tvEvent.Card.RecordingScheduleId;
          if (scheduleId > 0)
          {
            Recording rec = Recording.ActiveRecording(scheduleId);
            Log.Info("TsBufferExtractor: Detected a started recording on {0}, ProgramName: {1}", _tvEvent.User.Name, rec.Title);
            InitiateBufferFilesCopyProcess(rec);
          }
        }
      }
      catch (Exception ex)
      {
        Log.Error("TsBufferExtractor.CheckRecordingStatus exception : {0}", ex);
      }
    }

    private void InitiateBufferFilesCopyProcess(Recording rec)
    {
      if (_tsBufferExtractorSetup == "A" && _snapshotBufferPosition == -2)
      {
        Log.Info("TsBufferExtractor: there is no program information, skip the ts buffer copy.");
        return;
      }

      string recordingFilename = rec.FileName;
      IUser u = _tvEvent.Card.User;
      long bufferId = 0;
      Int64 currentPosition = -1;

      var layer = new TvBusinessLayer();
      Int64 maximumFileSize = Int64.Parse(layer.GetSetting("timeshiftMaxFileSize", "30").Value) * 1000 * 1000;
      int maxFiles = Convert.ToInt16(layer.GetSetting("timeshiftMaxFiles", "30").Value);

      List<string[]> itemlist = new List<string[]>();

      if (RemoteControl.Instance.TimeShiftGetCurrentFilePosition(ref u, ref currentPosition, ref bufferId))
      {
        string currentFile = RemoteControl.Instance.TimeShiftFileName(ref u) + bufferId.ToString() + ".ts";

        if (_snapshotBufferPosition == -2)
        {
          _snapshotBufferId = bufferId + 1;
          Log.Debug("TsBufferExtractor: snapshotBufferPosition was overwritten, the new _snapshotBufferId {0}", _snapshotBufferId);
        }

        Log.Info("TsBufferExtractor: current TS Position {0}, TS bufferId {1}, snapshotBufferId {2}, recording file {3}",
          currentPosition, bufferId, _snapshotBufferId, recordingFilename);

        if (_snapshotBufferId < bufferId)
        {
          Log.Debug("TsBufferExtractor: snapshotBufferId {0}, bufferId {1}", _snapshotBufferId, bufferId);
          string nextFile;

          for (long i = _snapshotBufferId; i < bufferId; i++)
          {
            nextFile = RemoteControl.Instance.TimeShiftFileName(ref u) + i + ".ts";
            Log.Debug("TsBufferExtractor: nextFile {0}", nextFile);
            itemlist.Add(new[] { nextFile, string.Format("{0}", maximumFileSize), recordingFilename });
          }
        }
        else if (_snapshotBufferId > bufferId)
        {
          {
            string nextFile;

            for (long i = _snapshotBufferId; i <= maxFiles; i++)
            {
              nextFile = RemoteControl.Instance.TimeShiftFileName(ref u) + i + ".ts";
              Log.Debug("TsBufferExtractor: nextFile {0}", nextFile);
              itemlist.Add(new[] { nextFile, string.Format("{0}", maximumFileSize), recordingFilename });
            }

            if (1 < _bufferId)
            {
              for (long i = 1; i < _bufferId; i++)
              {
                nextFile = RemoteControl.Instance.TimeShiftFileName(ref u) + i + ".ts";
                Log.Debug("TsBufferExtractor: nextFile {0}", nextFile);
                itemlist.Add(new[] { nextFile, string.Format("{0}", maximumFileSize), recordingFilename });
              }
            }
          }
        }
        itemlist.Add(new[] { currentFile, string.Format("{0}", currentPosition), recordingFilename });
        Log.Debug("TsBufferExtractor: currentFile {0}", currentFile);

        try
        {
          Schedule newSchedule = new Schedule(rec.IdChannel, rec.Title, rec.StartTime, DateTime.Now);
          newSchedule.PreRecordInterval = 0;
          newSchedule.PostRecordInterval = 0;
          newSchedule.Persist();

          Copyer Copy = new Copyer();

          Copy.CopyTimeShiftFile(itemlist, rec, newSchedule);
        }
        catch (Exception ex)
        {
          Log.Error("TsBufferExtractor.CopyTimeShiftFile exception : {0}", ex);
        }
      }
      _snapshotBufferPosition = -1;
      _snapshotBufferFile = string.Empty;
      _snapshotBufferId = 0;
    }

    private void SnapshotTimeShiftBuffer()
    {
      IUser u = _tvEvent.Card.User;
      if (u == null)
      {
        Log.Error("TsBufferExtractor.SnapshotTimeShiftBuffer: Snapshot buffer failed. TvHome.Card.User==null");
        _errors++;
        return;
      }

      Log.Debug("TsBufferExtractor.SnapshotTimeShiftBuffer: Snapshotting timeshift buffer. User: {0}", _tvEvent.User.Name);

      if (_idChannelToWatch == -1 || !_tvEvent.Card.IsTimeShifting)
      {
        Log.Debug("TsBufferExtractor.SnapshotTimeShiftBuffer: not timeshifting");
        _errors++;
        return;
      }

      if (!RemoteControl.Instance.TimeShiftGetCurrentFilePosition(ref u, ref _snapshotBufferPosition, ref _snapshotBufferId))
      {
        Log.Debug("TsBufferExtractor.SnapshotTimeShiftBuffer: TimeShiftGetCurrentFilePosition failed.");
        _errors++;
        return;
      }
      _snapshotBufferFile = RemoteControl.Instance.TimeShiftFileName(ref u) + _snapshotBufferId.ToString() + ".ts";
      Log.Info("TsBufferExtractor.SnapshotTimeShiftBuffer: Snapshot done - position: {0}, filename: {1}", _snapshotBufferPosition, _snapshotBufferFile);
    }

    private void CheckOrUpdateTimeShiftPosition()
    {
      if (_idChannelToWatch == -1 || !_tvEvent.Card.IsTimeShifting)
      {
        Log.Debug("TsBufferExtractor: not timeshifting");
        _errors++;
        return;
      }

      Channel chan = Channel.Retrieve(_idChannelToWatch);

      if (chan == null || chan.CurrentProgram == null)
      {
        Log.Debug("TsBufferExtractor: no EPG data, returning");
        return;
      }

      if (_tsBufferExtractorSetup != "C")
      {
        try
        {
          DateTime current = DateTime.Now;
          current = current.AddMinutes((double)_preRecordInterval);
          current = new DateTime(current.Year, current.Month, current.Day, current.Hour, current.Minute, 0);
          DateTime dtProgEnd = chan.CurrentProgram.EndTime;
          dtProgEnd = new DateTime(dtProgEnd.Year, dtProgEnd.Month, dtProgEnd.Day, dtProgEnd.Hour, dtProgEnd.Minute, 0);

          Log.Debug("TsBufferExtractor: Ch: ({5}) CurrentProgram ({0}) Checking {1} == {2}, _bufferId {3}, _snapshotBufferId {4}, user: {6}", chan.CurrentProgram.Title,
            current.ToString("dd.MM.yy HH:mm"), dtProgEnd.ToString("dd.MM.yy HH:mm"), _bufferId, _snapshotBufferId, chan.DisplayName, _tvEvent.User.Name);

          if (current == dtProgEnd)
          {
            Log.Debug("TsBufferExtractor: Next program starts within the configured Pre-Rec interval. Current program: ({0}) ending: {1}", chan.CurrentProgram.Title, chan.CurrentProgram.EndTime.ToString());
            SnapshotTimeShiftBuffer();
          }
        }
        catch (Exception ex)
        {
          Log.Error("TsBufferExtractor.CheckOrUpdateTimeShiftPosition exception : {0}", ex);
        }
      }
    }

    private void UpdateTimeShiftReusedStatus()
    {
      Int64 currentPosition = -1;

      _bufferId = GetTimeShiftPosition(ref currentPosition);

      if (_snapshotBufferId == _bufferId && _snapshotBufferPosition > currentPosition)
      {
        _snapshotBufferPosition = -2; //magic number
        _snapshotBufferId = 0;
        Log.Info("TsBufferExtractor: snapshot buffer is reused on {0}", _tvEvent.User.Name);
      }

    }

    private long GetTimeShiftPosition(ref Int64 currentPosition)
    {
      if (!_tvEvent.Card.IsTimeShifting)
        return 0;

      IUser u = _tvEvent.Card.User;

      long bufferId = 0;

      try
      {
        if (RemoteControl.Instance.TimeShiftGetCurrentFilePosition(ref u, ref currentPosition, ref bufferId))
        {
          return bufferId;
        }
      }
      catch
      {
        Log.Error("TsBufferExtractor: error in GetTimeShiftPosition");
      }

      return 0;
    }
    #endregion
  }
}