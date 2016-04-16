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
    
    public void CopyTimeShiftFile(object itemlist)
    {
      try
      {
        Thread _CopyTimeShiftFile;
        _CopyTimeShiftFile = new Thread(TsCopier);
        _CopyTimeShiftFile.Priority = ThreadPriority.Lowest;
        _CopyTimeShiftFile.IsBackground = true;
        _CopyTimeShiftFile.Start(itemlist);
      }
      catch (Exception ex)
      {
        Log.Error("TsCopier exception: {0}", ex);
        return;
      }
    }

    private void TsCopier(object itemlist)
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

          if (success)
          {
            Thread mergefilesThread;
            mergefilesThread = new Thread(mergeThread);
            mergefilesThread.Priority = ThreadPriority.Lowest;
            mergefilesThread.IsBackground = true;
            mergefilesThread.Start(_filename);
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
      }
      catch (Exception ex)
      {
        Log.Error("MergeFiles: Exception: {0}", ex);
      }
    }
  }
}
