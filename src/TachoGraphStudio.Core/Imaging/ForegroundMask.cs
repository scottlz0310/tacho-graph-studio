using OpenCvSharp;

namespace TachoGraphStudio.Core.Imaging;

// 円盤分割・背景除去で共通の前景判定(GIMP 版実績値)
internal static class ForegroundMask
{
    // 前景判定 nonwhite = 255 - min(B,G,R) >= threshold は min(B,G,R) <= 255 - threshold と等価
    public static Mat Build(Mat bgr, int threshold)
    {
        Mat[] channels = Cv2.Split(bgr);
        try
        {
            using Mat minChannel = new();
            Cv2.Min(channels[0], channels[1], minChannel);
            Cv2.Min(minChannel, channels[2], minChannel);

            Mat mask = new();
            Cv2.Threshold(minChannel, mask, 255 - threshold, 255, ThresholdTypes.BinaryInv);
            return mask;
        }
        finally
        {
            foreach (Mat channel in channels)
            {
                channel.Dispose();
            }
        }
    }
}
