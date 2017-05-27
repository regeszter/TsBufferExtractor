using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.IO;
using TvDatabase;
using TvLibrary.Interfaces;
using TvLibrary.Log;
using TvEngine.Events;
using TvControl;
using TvService;


namespace TsBufferExtractor
{
  public class Copyer
  {
    string _filename;
    bool _startMerge = false;

    public Copyer()
    {
      ITvServerEvent events = GlobalServiceProvider.Instance.Get<ITvServerEvent>();
      events.OnTvServerEvent += new TvServerEventHandler(events_OnTvServerEvent);
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

      if (tvEvent.EventType == TvServerEventType.RecordingEnded && _filename == tvEvent.Recording.FileName)
      {
        _startMerge = true;

        ITvServerEvent events = GlobalServiceProvider.Instance.Get<ITvServerEvent>();
        events.OnTvServerEvent -= new TvServerEventHandler(events_OnTvServerEvent);
      }
    }

    #endregion
    
    public void CopyTimeShiftFile(object itemlist, Recording rec, Schedule newSchedule)
    {
      try
      {
        ThreadStart ts = delegate ()
        {
          TsCopier(itemlist, rec, newSchedule);
        };

        Thread _CopyTimeShiftFile;
        _CopyTimeShiftFile = new Thread(ts);
        _CopyTimeShiftFile.Priority = ThreadPriority.Lowest;
        _CopyTimeShiftFile.IsBackground = true;
        _CopyTimeShiftFile.Start();
      }
      catch (Exception ex)
      {
        Log.Error("TsCopier exception: {0}", ex);
        return;
      }
    }

    private void TsCopier(object itemlist, Recording rec, Schedule newSchedule)
    {
      string[] bufferListObject;
      bufferListObject = new string[3];
      List<string[]> _itemlist = (List<string[]>)itemlist;
      bool foundHeader = false;
      bufferListObject = _itemlist[0];
      string targetTs = Path.GetDirectoryName(bufferListObject[2]) + "\\" + Path.GetFileNameWithoutExtension(bufferListObject[2]) + "_buffer.ts";
      bool success = false;
      _filename = bufferListObject[2];

      try
      {
        Log.Info("TsCopier: targetTs {0}", targetTs);

        using (FileStream writer = new FileStream(targetTs, FileMode.CreateNew, FileAccess.Write))
        {
          for (int i = 0; i < _itemlist.Count; i++)
          {
            bufferListObject = _itemlist[i];

            try
            {
              if (File.Exists(bufferListObject[0]))
              {
                using (FileStream reader = new FileStream(bufferListObject[0], FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                  Log.Info("TsCopier: TSfilename {0}", bufferListObject[0]);
                  Log.Debug("TsCopier: TSfilename filesize {0}", bufferListObject[1]);

                  if (!foundHeader)
                  {
                    byte[] prebuf = new byte[1024 * 1024];
                    int bytesPreRead;
                    bytesPreRead = reader.Read(prebuf, 0, 1024 * 1024);
                    long position = 0;

                    // find TS packet header
                    while (bytesPreRead > 0 && !foundHeader)
                    {
                      for (int x = 0; x < 1024 * 1024 - 376; x++)
                      {
                        if (prebuf[x] == 0x47 && prebuf[x + 188] == 0x47 && prebuf[x + 376] == 0x47)
                        {
                          Log.Debug("TsCopier: TS packet header found at {0} pos in {1}.", x, bufferListObject[0]);
                          position = x;
                          foundHeader = true;
                          break;
                        }
                      }
                      bytesPreRead = reader.Read(prebuf, 0, 1024 * 1024);
                    }

                    reader.Position = position;

                    if (!foundHeader)
                    {
                      Log.Debug("TsCopier: TS packet header not found in {0}.", bufferListObject[0]);
                      break;
                    }
                  }

                  byte[] buf = new byte[1024 * 1024];
                  int bytesRead = reader.Read(buf, 0, 1024 * 1024);
                  while (bytesRead > 0)
                  {
                    if (reader.Position > Convert.ToInt64(bufferListObject[1]))
                      bytesRead -= (int)(reader.Position - Convert.ToInt64(bufferListObject[1]));

                    if (bytesRead <= 0)
                      break;

                    writer.Write(buf, 0, bytesRead);
                    bytesRead = reader.Read(buf, 0, 1024 * 1024);
                    Thread.Sleep(100);
                  }
                  reader.Close();
                }
              }
              success = true;
            }
            catch (Exception ex)
            {
              Log.Error("TsCopier exception: {0}", ex);
            }
          }
          writer.Flush();
          writer.Close();

          var layer = new TvBusinessLayer();
          String TsBufferExtractorFileSetup = layer.GetSetting("TsBufferExtractorFileSetup", "A").Value;

          if (success && (TsBufferExtractorFileSetup == "B" || TsBufferExtractorFileSetup == "C"))
          {
            Thread mergefilesThread;
            mergefilesThread = new Thread(mergeThread);
            mergefilesThread.Priority = ThreadPriority.Lowest;
            mergefilesThread.IsBackground = true;
            mergefilesThread.Start(_filename);
          }

          if (success && (TsBufferExtractorFileSetup == "A" || TsBufferExtractorFileSetup == "C"))
          {
            try
            {
              Log.Debug("TsCopier: Creating Recording entry for {0}", targetTs);

              RecordingDetail recDetail = new RecordingDetail(newSchedule, newSchedule.ReferencedChannel(), DateTime.Now, false);

              recDetail.Recording = new Recording(recDetail.Schedule.IdChannel, recDetail.Schedule.IdSchedule, false,
                                                rec.StartTime, DateTime.Now, rec.Title + " (from buffer)",
                                                recDetail.Program.Description, recDetail.Program.Genre, targetTs,
                                                recDetail.Schedule.KeepMethod,
                                                recDetail.Schedule.KeepDate, 0, rec.IdServer, recDetail.Program.EpisodeName,
                                                recDetail.Program.SeriesNum, recDetail.Program.EpisodeNum,
                                                recDetail.Program.EpisodePart);

              recDetail.Recording.Persist();

              IUser user = recDetail.User;

              TsBufferExtractor.Controller.Fire(this, new TvServerEventArgs(TvServerEventType.RecordingEnded, new VirtualCard(user), (User)user,
                                                       recDetail.Schedule, recDetail.Recording));

              MatroskaTagInfo info = new MatroskaTagInfo();
              info.title = rec.Title + " (from buffer)";
              info.description = recDetail.Program.Description;
              info.genre = recDetail.Program.Genre;

              info.channelName = recDetail.Schedule.ReferencedChannel().DisplayName;
              info.episodeName = recDetail.Program.EpisodeName;
              info.seriesNum = recDetail.Program.SeriesNum;
              info.episodeNum = recDetail.Program.EpisodeNum;
              info.episodePart = recDetail.Program.EpisodePart;
              info.startTime = rec.StartTime;
              info.endTime = DateTime.Now;

              MatroskaTagHandler.WriteTag(Path.ChangeExtension(targetTs, ".xml"), info);
            }
            catch (Exception ex)
            {
              Log.Error("TsCopier Exception: {0}", ex);
            }
          }

          if (success && TsBufferExtractorFileSetup == "C")
          {
            try
            {
              String mergedName = _filename + "_merged.ts";

              Log.Debug("TsCopier: Creating Recording entry for {0}", mergedName);

              RecordingDetail recDetail = new RecordingDetail(newSchedule, newSchedule.ReferencedChannel(), DateTime.Now, false);

              recDetail.Recording = new Recording(recDetail.Schedule.IdChannel, recDetail.Schedule.IdSchedule, false,
                                                rec.StartTime, DateTime.Now, rec.Title + " (merged)",
                                                recDetail.Program.Description, recDetail.Program.Genre, mergedName,
                                                recDetail.Schedule.KeepMethod,
                                                recDetail.Schedule.KeepDate, 0, rec.IdServer, recDetail.Program.EpisodeName,
                                                recDetail.Program.SeriesNum, recDetail.Program.EpisodeNum,
                                                recDetail.Program.EpisodePart);

              recDetail.Recording.Persist();

              IUser user = recDetail.User;

              TsBufferExtractor.Controller.Fire(this, new TvServerEventArgs(TvServerEventType.RecordingEnded, new VirtualCard(user), (User)user,
                                                       recDetail.Schedule, recDetail.Recording));

              MatroskaTagInfo info = new MatroskaTagInfo();
              info.title = rec.Title + " (merged)";
              info.description = recDetail.Program.Description;
              info.genre = recDetail.Program.Genre;

              info.channelName = recDetail.Schedule.ReferencedChannel().DisplayName;
              info.episodeName = recDetail.Program.EpisodeName;
              info.seriesNum = recDetail.Program.SeriesNum;
              info.episodeNum = recDetail.Program.EpisodeNum;
              info.episodePart = recDetail.Program.EpisodePart;
              info.startTime = rec.StartTime;
              info.endTime = DateTime.Now;

              MatroskaTagHandler.WriteTag(Path.ChangeExtension(mergedName, ".xml"), info);
            }
            catch (Exception ex)
            {
              Log.Error("TsCopier Exception: {0}", ex);
            }
          }

          Log.Info("TsCopier: Done {0}", targetTs);
        }
      }
      catch (Exception ex)
      {
        Log.Error("TsCopier Exception: {0}", ex);
      }
    }

    private void mergeThread(object FileName)
    {
      string fName = (string)FileName;

      while (!_startMerge)
      {
        //Log.Debug("TsCopier:MergeThread: waiting for the end of the recording of {0}", fName);
        Thread.Sleep(1000);
      }

      Merge(fName);
    }

    private void Merge(string FileName)
    {
      try
      {
        Log.Info("MergeFiles: Start {0}", FileName);

        string currentPath = System.Reflection.Assembly.GetCallingAssembly().Location;

        FileInfo currentPathInfo = new FileInfo(currentPath);
        string bufferName = Path.GetDirectoryName(FileName) + "\\" + Path.GetFileNameWithoutExtension(FileName) + "_buffer.ts";

        string cmd = string.Format(" -i \"concat:{0}|{1}\" -c copy \"{1}_merged.ts\"", bufferName, FileName);
        string ffmpeg = currentPathInfo.DirectoryName.Remove(currentPathInfo.DirectoryName.Length - 8) + "\\" + "ffmpeg.exe";

        Log.Debug("MergeFiles: {0}{1}", ffmpeg, cmd);

        Process process = new Process();
        process.StartInfo.FileName = ffmpeg;
        process.StartInfo.Arguments = cmd;
        process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
        process.Start();
        process.PriorityClass = ProcessPriorityClass.BelowNormal;
        process.WaitForExit(1000 * 60 * 5);

        var layer = new TvBusinessLayer();
        String TsBufferExtractorFileSetup = layer.GetSetting("TsBufferExtractorFileSetup", "A").Value;

        if (TsBufferExtractorFileSetup=="B")
        {
          Log.Debug("Remove: FileName {0}", FileName);

          File.Delete(FileName);

          Log.Debug("Remove: FileName {0}", bufferName);

          File.Delete(bufferName);

          String mergedName = FileName + "_merged.ts";

          Log.Debug("Rename: {0} to {1}", mergedName, FileName);

          File.Move(mergedName, FileName);
        }
      }
      catch (Exception ex)
      {
        Log.Error("MergeFiles: Exception: {0}", ex);
      }
    }
  }
}
