using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TsBufferExtractor.Interface;


namespace TsBufferExtractor
{
  public class TsBufferExtractorServer : MarshalByRefObject, TsBufferExtractorInterface
  {
    public void ManualRecordingStarted(string strMessage)
    {
      TvTimeShiftPositionWatcher.IsManual = strMessage;
    }
  }
}
