using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.IO;
using Gentle.Common;
using TvControl;
using TvDatabase;
using TvEngine.Interfaces;
using TvLibrary.Interfaces;
using TvLibrary.Log;
using TvEngine.Events;
using TvService;


namespace TsBufferExtractor
{
  public class Copyer
  {
    public void CopyTimeShiftFile(object itemlist, Recording rec, Schedule newSchedule)
    {
      try
      {
        ThreadStart ts = delegate()
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
            }
            catch (Exception ex)
            {
              Log.Error("TsCopier exception: {0}", ex);
            }
          }
          writer.Flush();
          writer.Close();
          Log.Info("TsCopier: Done {0}", targetTs);
        }
      }
      catch (Exception ex)
      {
        Log.Error("TsCopier Exception: {0}", ex);
      }

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

        MatroskaTagHandler.WriteTag(System.IO.Path.ChangeExtension(targetTs, ".xml"), info);
        Log.Info("TsCopier: Finished the job.");
      }
      catch (Exception ex)
      {
        Log.Error("TsCopier Exception: {0}", ex);
      }
    }

  }
}
