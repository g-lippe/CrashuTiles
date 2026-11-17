// Ulanzi D200 probe: talks to interface 0 over raw Win32 HID (no installs).
// Protocol per redphx/strmdck + jcalado/companion-surface-d200.
// C# 5 (Windows PowerShell 5.1 Add-Type compiler).
using System;
using System.IO;
using System.IO.Compression;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public class D200Probe {
  [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
  static extern SafeFileHandle CreateFile(string n, uint a, uint s, IntPtr sa, uint c, uint f, IntPtr t);
  [DllImport("kernel32.dll", SetLastError = true)]
  static extern bool ReadFile(SafeFileHandle h, byte[] b, int n, out int r, IntPtr o);
  [DllImport("kernel32.dll", SetLastError = true)]
  static extern bool WriteFile(SafeFileHandle h, byte[] b, int n, out int w, IntPtr o);
  [DllImport("kernel32.dll")]
  static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
  [StructLayout(LayoutKind.Sequential)]
  struct MEMSTAT { public uint len, load; public ulong a, b, c, d, e, f, g; }
  [DllImport("kernel32.dll")]
  static extern bool GlobalMemoryStatusEx(ref MEMSTAT m);

  const int PKT = 1024, HDR = 8, FIRST = PKT - HDR;
  const int CMD_SET_BUTTONS = 0x0001, CMD_PARTIAL = 0x000d, CMD_SMALL = 0x0006, CMD_BRIGHT = 0x000a;
  const int IN_BUTTON = 0x0101, IN_INFO = 0x0303, IN_ACK = 0x010b;

  SafeFileHandle hr, hw;
  Stopwatch clock = Stopwatch.StartNew();
  AutoResetEvent ackEvt = new AutoResetEvent(false);
  Queue<int[]> presses = new Queue<int[]>();
  int ackCount;
  bool verboseAck = true;

  void Log(string s) { Console.WriteLine(string.Format("[{0,7:F0} ms] {1}", clock.Elapsed.TotalMilliseconds, s)); }

  public D200Probe(string path) {
    hr = CreateFile(path, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
    if (hr.IsInvalid) throw new Exception("open(read) failed, win32 error " + Marshal.GetLastWin32Error());
    hw = CreateFile(path, 0x40000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
    if (hw.IsInvalid) throw new Exception("open(write) failed, win32 error " + Marshal.GetLastWin32Error());
    var t = new Thread(ReadLoop); t.IsBackground = true; t.Start();
  }

  // ---------- receive ----------
  void ReadLoop() {
    var buf = new byte[PKT + 1];
    while (true) {
      int n;
      if (!ReadFile(hr, buf, buf.Length, out n, IntPtr.Zero)) { Log("read error " + Marshal.GetLastWin32Error()); return; }
      // buf[0] is the HID report ID (0); protocol bytes start at 1
      if (buf[1] != 0x7c || buf[2] != 0x7c) { Log("RX non-framed: " + Hex(buf, 1, 16)); continue; }
      int cmd = (buf[3] << 8) | buf[4];
      int len = BitConverter.ToInt32(buf, 5);
      if (cmd == IN_ACK) {
        Interlocked.Increment(ref ackCount);
        if (verboseAck) Log("RX ACK len=" + len + " data=" + Hex(buf, 9, Math.Min(len, 12)));
        ackEvt.Set();
      } else if (cmd == IN_BUTTON) {
        int state = buf[9], idx = buf[10], type = buf[11], act = buf[12];
        string a = act == 1 ? "PRESS" : act == 0 ? "release" : act == 2 ? "left" : act == 3 ? "right" : ("act" + act);
        Log(string.Format("RX BUTTON idx={0} (col {1}, row {2}) {3} type={4} state={5}", idx, idx % 5, idx / 5, a, type, state));
        if (act == 1) lock (presses) presses.Enqueue(new int[] { idx, (int)clock.ElapsedMilliseconds });
      } else if (cmd == IN_INFO) {
        Log("RX DEVICE INFO: " + Encoding.ASCII.GetString(buf, 9, Math.Max(0, Math.Min(len, PKT - HDR))).TrimEnd('\0'));
      } else {
        Log(string.Format("RX cmd=0x{0:X4} len={1} data={2}", cmd, len, Hex(buf, 9, Math.Min(Math.Max(len, 0), 24))));
      }
    }
  }

  static string Hex(byte[] b, int off, int n) {
    var sb = new StringBuilder();
    for (int i = 0; i < n && off + i < b.Length; i++) sb.Append(b[off + i].ToString("x2"));
    return sb.ToString();
  }

  // ---------- send ----------
  void WriteRaw(byte[] pkt1024) {
    var outb = new byte[PKT + 1];
    Buffer.BlockCopy(pkt1024, 0, outb, 1, PKT);
    int w;
    if (!WriteFile(hw, outb, outb.Length, out w, IntPtr.Zero)) throw new Exception("write failed " + Marshal.GetLastWin32Error());
  }

  static byte[] Frame(int cmd, int totalLen, byte[] data, int off, int count) {
    var p = new byte[PKT];
    p[0] = 0x7c; p[1] = 0x7c; p[2] = (byte)(cmd >> 8); p[3] = (byte)cmd;
    BitConverter.GetBytes(totalLen).CopyTo(p, 4);
    Buffer.BlockCopy(data, off, p, HDR, count);
    return p;
  }

  void SendSimple(int cmd, byte[] payload) { WriteRaw(Frame(cmd, payload.Length, payload, 0, payload.Length)); }

  // returns packet count
  int SendChunked(int cmd, byte[] payload) {
    WriteRaw(Frame(cmd, payload.Length, payload, 0, Math.Min(FIRST, payload.Length)));
    int n = 1;
    for (int off = FIRST; off < payload.Length; off += PKT) {
      var p = new byte[PKT];
      Buffer.BlockCopy(payload, off, p, 0, Math.Min(PKT, payload.Length - off));
      WriteRaw(p); n++;
    }
    return n;
  }

  // ---------- rendering ----------
  static byte[] Png(Bitmap bmp) { using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); return ms.ToArray(); } }

  static byte[] Tile(string big, string small, Color bg) {
    using (var bmp = new Bitmap(196, 196))
    using (var g = Graphics.FromImage(bmp)) {
      g.SmoothingMode = SmoothingMode.AntiAlias;
      g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
      using (var b = new LinearGradientBrush(new Rectangle(0, 0, 196, 196), bg, Color.FromArgb(bg.R / 3, bg.G / 3, bg.B / 3), 90f))
        g.FillRectangle(b, 0, 0, 196, 196);
      var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
      using (var f = new Font("Segoe UI", 54, FontStyle.Bold, GraphicsUnit.Pixel))
        g.DrawString(big, f, Brushes.White, new RectangleF(0, 20, 196, 110), sf);
      using (var f = new Font("Segoe UI", 22, FontStyle.Regular, GraphicsUnit.Pixel))
        g.DrawString(small, f, Brushes.White, new RectangleF(0, 125, 196, 50), sf);
      return Png(bmp);
    }
  }

  static Color Hue(int i, int n) {
    double h = 360.0 * i / n, s = 0.65, v = 0.85;
    double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c;
    double r = 0, gg = 0, b = 0;
    if (h < 60) { r = c; gg = x; } else if (h < 120) { r = x; gg = c; } else if (h < 180) { gg = c; b = x; }
    else if (h < 240) { gg = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
    return Color.FromArgb((int)((r + m) * 255), (int)((gg + m) * 255), (int)((b + m) * 255));
  }

  // ---------- zip / manifest ----------
  const string FONT = "{\"Align\":\"bottom\",\"Color\":16777215,\"FontName\":\"Source Han Sans SC\",\"ShowTitle\":true,\"Size\":10,\"Weight\":80}";

  // tiles: idx -> png. Slot 3_2 is always the small window (mode smallMode).
  static byte[] BuildZip(Dictionary<int, byte[]> tiles, int smallMode, byte[] bgPng = null) {
    var rnd = new Random();
    for (int attempt = 0; attempt < 64; attempt++) {
      var images = new Dictionary<string, byte[]>();
      var man = new StringBuilder("{");
      bool first = true;
      foreach (var kv in tiles) {
        int col = kv.Key % 5, row = kv.Key / 5;
        if (row == 2 && col >= 3) continue;
        string id = Guid.NewGuid().ToString();
        images[id] = kv.Value;
        if (!first) man.Append(","); first = false;
        man.AppendFormat("\"{0}_{1}\":{{\"State\":0,\"ViewParam\":[{{\"Font\":{2},\"Icon\":\"Images/{3}.png\",\"Text\":\"\"}}]}}", col, row, FONT, id);
      }
      if (!first) man.Append(",");
      if (bgPng != null) {
        string bid = Guid.NewGuid().ToString();
        images[bid] = bgPng;
        man.AppendFormat("\"3_2\":{{\"State\":0,\"SmallViewMode\":2,\"ViewParam\":[{{\"Font\":{0},\"Icon\":\"Images/{1}.png\",\"Text\":\"\"}}]}}", FONT, bid);
      } else
      man.AppendFormat("\"3_2\":{{\"State\":0,\"SmallViewMode\":{0},\"ViewParam\":[{{\"Font\":{1},\"Text\":\"\"}}]}}", smallMode, FONT);
      man.Append("}");

      byte[] zip;
      using (var ms = new MemoryStream()) {
        using (var za = new ZipArchive(ms, ZipArchiveMode.Create, true)) {
          AddEntry(za, "manifest.json", Encoding.UTF8.GetBytes(man.ToString()));
          if (attempt > 0) {
            var d = new StringBuilder();
            for (int i = 0; i < 8 * attempt; i++) d.Append((char)('a' + rnd.Next(26)));
            AddEntry(za, "dummy.txt", Encoding.ASCII.GetBytes(d.ToString()));
          }
          foreach (var kv in images) AddEntry(za, "Images/" + kv.Key + ".png", kv.Value);
        }
        zip = ms.ToArray();
      }
      bool safe = true;
      for (int i = FIRST; i < zip.Length; i += PKT) if (zip[i] == 0x00 || zip[i] == 0x7c) { safe = false; break; }
      if (safe) return zip;
    }
    throw new Exception("could not build a firmware-safe zip");
  }

  static void AddEntry(ZipArchive za, string name, byte[] data) {
    var e = za.CreateEntry(name, CompressionLevel.Fastest);
    using (var s = e.Open()) s.Write(data, 0, data.Length);
  }

  // ---------- stats ----------
  long pIdle, pKern, pUser;
  int Cpu() {
    long i, k, u; GetSystemTimes(out i, out k, out u);
    long di = i - pIdle, dt = (k - pKern) + (u - pUser);
    pIdle = i; pKern = k; pUser = u;
    return dt > 0 ? (int)Math.Round(100.0 * (dt - di) / dt) : 0;
  }
  static int Mem() { var m = new MEMSTAT(); m.len = (uint)Marshal.SizeOf(typeof(MEMSTAT)); GlobalMemoryStatusEx(ref m); return (int)m.load; }

  void SendSmall(int mode, int cpu, int mem, int gpu) {
    string s = string.Format("{0}|{1}|{2}|{3}|{4}|24H|", mode, cpu, mem, DateTime.Now.ToString("HH:mm:ss"), gpu);
    SendSimple(CMD_SMALL, Encoding.UTF8.GetBytes(s));
  }

  // send a zip and time it: write time and time until the device ACKs
  string Timed(int cmd, byte[] zip) {
    ackEvt.Reset();
    var t0 = clock.Elapsed.TotalMilliseconds;
    int pk = SendChunked(cmd, zip);
    var t1 = clock.Elapsed.TotalMilliseconds;
    bool acked = ackEvt.WaitOne(3000);
    var t2 = clock.Elapsed.TotalMilliseconds;
    return string.Format("{0,6} B / {1,3} pkts  write {2,6:F1} ms  ack {3}", zip.Length, pk, t1 - t0, acked ? (t2 - t0).ToString("F1") + " ms" : "none (3 s timeout)");
  }

  // 458x196 small-window image: a fake scrolling CPU graph with a counter
  static byte[] Wide(int frame) {
    using (var bmp = new Bitmap(458, 196))
    using (var g = Graphics.FromImage(bmp)) {
      g.SmoothingMode = SmoothingMode.AntiAlias;
      g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
      g.Clear(Color.FromArgb(12, 14, 20));
      var pts = new List<PointF>();
      for (int x = 0; x <= 458; x += 6) {
        double v = 0.5 + 0.3 * Math.Sin((x + frame * 12) / 40.0) + 0.15 * Math.Sin((x + frame * 12) / 13.0);
        pts.Add(new PointF(x, (float)(186 - v * 140)));
      }
      var poly = new List<PointF>(pts); poly.Add(new PointF(458, 196)); poly.Add(new PointF(0, 196));
      using (var b = new SolidBrush(Color.FromArgb(70, 60, 180, 255))) g.FillPolygon(b, poly.ToArray());
      using (var p = new Pen(Color.FromArgb(60, 180, 255), 3)) g.DrawLines(p, pts.ToArray());
      using (var f = new Font("Segoe UI", 34, FontStyle.Bold, GraphicsUnit.Pixel))
        g.DrawString("frame " + frame, f, Brushes.White, 14, 8);
      return Png(bmp);
    }
  }

  void SendNoWait(int cmd, byte[] zip) { SendChunked(cmd, zip); }

  public static void Bench(string path) {
    var d = new D200Probe(path);
    d.verboseAck = true;
    Thread.Sleep(300);

    // wait for the user: every key says "press to start"
    var wait = new Dictionary<int, byte[]>();
    for (int i = 0; i < 13; i++) wait[i] = Tile("GO?", "press to start", Color.FromArgb(90, 90, 90));
    d.SendNoWait(CMD_SET_BUTTONS, BuildZip(wait, 203));
    d.Log("waiting up to 3 min for a key press on the dock");
    var deadline = d.clock.ElapsedMilliseconds + 180000;
    bool go = false;
    while (!go && d.clock.ElapsedMilliseconds < deadline) {
      lock (d.presses) if (d.presses.Count > 0) { d.presses.Clear(); go = true; }
      if (!go) { if (d.clock.ElapsedMilliseconds % 1000 < 20) d.SendSmall(203, 0, 0, 0); Thread.Sleep(15); }
    }
    if (!go) { d.Log("no press, giving up"); return; }
    d.Log("started by key press");
    var countdown = new Dictionary<int, byte[]>();
    for (int n = 3; n >= 1; n--) {
      for (int i = 0; i < 13; i++) countdown[i] = Tile(n.ToString(), "get ready", Color.FromArgb(200, 140, 40));
      d.SendNoWait(CMD_PARTIAL, BuildZip(countdown, 203));
      Thread.Sleep(1000);
    }

    // A: one key counting 1..60 flat out
    d.Log("A: key 0 counts 1..60 as fast as possible");
    var sw = Stopwatch.StartNew(); double send = 0;
    for (int k = 1; k <= 60; k++) {
      var one = new Dictionary<int, byte[]>(); one[0] = Tile(k.ToString(), "flat out", Color.FromArgb(200, 60, 60));
      var z = BuildZip(one, 0);
      var t = sw.Elapsed.TotalMilliseconds; d.SendNoWait(CMD_PARTIAL, z); send += sw.Elapsed.TotalMilliseconds - t;
    }
    d.Log(string.Format("A: {0:F0} ms total, {1:F1} ms/frame, of which sending {2:F1} ms/frame", sw.Elapsed.TotalMilliseconds, sw.Elapsed.TotalMilliseconds / 60, send / 60));
    Thread.Sleep(2500);

    // B: key 1 counts 1..30 at a paced 10 fps
    d.Log("B: key 1 counts 1..30 at 10 fps");
    sw.Restart();
    for (int k = 1; k <= 30; k++) {
      var one = new Dictionary<int, byte[]>(); one[1] = Tile(k.ToString(), "10 fps", Color.FromArgb(60, 160, 80));
      d.SendNoWait(CMD_PARTIAL, BuildZip(one, 0));
      while (sw.ElapsedMilliseconds < k * 100) Thread.Sleep(1);
    }
    d.Log("B: done in " + sw.ElapsedMilliseconds + " ms");
    Thread.Sleep(2500);

    // C: all 13 keys, 20 frames flat out; split render / zip / send
    d.Log("C: all 13 keys show frame 1..20 as fast as possible");
    double tr = 0, tz = 0, ts = 0; sw.Restart();
    for (int k = 1; k <= 20; k++) {
      var t0 = sw.Elapsed.TotalMilliseconds;
      var all = new Dictionary<int, byte[]>();
      for (int i = 0; i < 13; i++) all[i] = Tile(k.ToString(), "all keys", Hue(i + k, 13));
      var t1 = sw.Elapsed.TotalMilliseconds;
      var z = BuildZip(all, 0);
      var t2 = sw.Elapsed.TotalMilliseconds;
      d.SendNoWait(CMD_PARTIAL, z);
      var t3 = sw.Elapsed.TotalMilliseconds;
      tr += t1 - t0; tz += t2 - t1; ts += t3 - t2;
    }
    d.Log(string.Format("C: {0:F1} ms/frame (render {1:F1}, zip {2:F1}, send {3:F1})", sw.Elapsed.TotalMilliseconds / 20, tr / 20, tz / 20, ts / 20));
    Thread.Sleep(2500);

    // D: small window as a custom 458x196 image, animated at ~4 fps via partial updates
    d.Log("D: small screen shows a moving graph, frame 1..40 at ~4 fps");
    var tiles = new Dictionary<int, byte[]>();
    for (int i = 0; i < 13; i++) tiles[i] = Tile(i.ToString(), "bench done", Hue(i, 13));
    d.SendNoWait(CMD_SET_BUTTONS, BuildZip(tiles, 2, Wide(0)));
    Thread.Sleep(500);
    d.SendSmall(2, 0, 0, 0);
    sw.Restart();
    for (int k = 1; k <= 40; k++) {
      d.SendNoWait(CMD_PARTIAL, BuildZip(new Dictionary<int, byte[]>(), 2, Wide(k)));
      while (sw.ElapsedMilliseconds < k * 250) Thread.Sleep(1);
    }
    d.Log("D: done; small-window zip ~" + BuildZip(new Dictionary<int, byte[]>(), 2, Wide(41)).Length + " B");
    // keep the background mode alive a little so it can be looked at
    for (int s = 0; s < 8; s++) { d.SendSmall(2, 0, 0, 0); Thread.Sleep(1000); }
    d.Log("acks received: " + d.ackCount);
  }

  bool WaitForGo() {
    var wait = new Dictionary<int, byte[]>();
    for (int i = 0; i < 13; i++) wait[i] = Tile("GO?", "press to start", Color.FromArgb(90, 90, 90));
    SendNoWait(CMD_SET_BUTTONS, BuildZip(wait, 203));
    Log("waiting up to 3 min for a key press on the dock");
    var deadline = clock.ElapsedMilliseconds + 180000;
    long nextClock = 0;
    while (clock.ElapsedMilliseconds < deadline) {
      lock (presses) if (presses.Count > 0) { presses.Clear(); Log("started by key press"); break; }
      if (clock.ElapsedMilliseconds >= nextClock) { SendSmall(203, 0, 0, 0); nextClock = clock.ElapsedMilliseconds + 1000; }
      Thread.Sleep(15);
    }
    if (clock.ElapsedMilliseconds >= deadline) { Log("no press, giving up"); return false; }
    var cd = new Dictionary<int, byte[]>();
    for (int n = 3; n >= 1; n--) {
      for (int i = 0; i < 13; i++) cd[i] = Tile(n.ToString(), "get ready", Color.FromArgb(200, 140, 40));
      SendNoWait(CMD_PARTIAL, BuildZip(cd, 203));
      Thread.Sleep(1000);
    }
    return true;
  }

  void AllKeys(string big, string small, Color c, int smallMode) {
    var t = new Dictionary<int, byte[]>();
    for (int i = 0; i < 13; i++) t[i] = Tile(big, small, c);
    SendNoWait(CMD_PARTIAL, BuildZip(t, smallMode));
  }

  // send one zip, return how long the write blocked (dock backpressure)
  double SendTimed(int cmd, byte[] zip) {
    var t = clock.Elapsed.TotalMilliseconds; SendChunked(cmd, zip); return clock.Elapsed.TotalMilliseconds - t;
  }

  static string Stats(List<double> v) {
    if (v.Count == 0) return "-";
    var s = new List<double>(v); s.Sort();
    double sum = 0; foreach (var x in s) sum += x;
    var sb = new StringBuilder();
    sb.AppendFormat("avg {0:F0} / min {1:F0} / max {2:F0} ms;  by tens:", sum / s.Count, s[0], s[s.Count - 1]);
    for (int i = 0; i < v.Count; i += 10) {
      double a = 0; int n = 0;
      for (int j = i; j < Math.Min(i + 10, v.Count); j++) { a += v[j]; n++; }
      sb.AppendFormat(" {0:F0}", a / n);
    }
    return sb.ToString();
  }

  // round 2: reproduce the black tiles (C) and the small-screen lag (D)
  public static void Bench2(string path) {
    var d = new D200Probe(path);
    Thread.Sleep(300);
    if (!d.WaitForGo()) return;
    var sends = new List<double>();

    // C1: all 13 keys flat out, 40 frames
    d.Log("C1: all 13 keys, 40 frames flat out");
    for (int k = 1; k <= 40; k++) {
      var all = new Dictionary<int, byte[]>();
      for (int i = 0; i < 13; i++) all[i] = Tile(k.ToString(), "C1 flat out", Hue(i + k, 13));
      sends.Add(d.SendTimed(CMD_PARTIAL, BuildZip(all, 203)));
    }
    d.Log("C1 send-block per frame: " + Stats(sends));
    Thread.Sleep(3000);

    // C2: all 13 keys paced at 150 ms, 40 frames
    d.Log("C2: all 13 keys, 40 frames paced 150 ms");
    sends.Clear();
    var sw = Stopwatch.StartNew();
    for (int k = 1; k <= 40; k++) {
      var all = new Dictionary<int, byte[]>();
      for (int i = 0; i < 13; i++) all[i] = Tile(k.ToString(), "C2 paced", Hue(i + k, 13));
      sends.Add(d.SendTimed(CMD_PARTIAL, BuildZip(all, 203)));
      while (sw.ElapsedMilliseconds < k * 150) Thread.Sleep(1);
    }
    d.Log("C2 send-block per frame: " + Stats(sends));
    Thread.Sleep(3000);

    // D1: small screen graph, 80 frames at 4 fps, then STOP on all keys
    d.Log("D1: small screen 80 frames at 4 fps");
    var bgTiles = new Dictionary<int, byte[]>();
    for (int i = 0; i < 13; i++) bgTiles[i] = Tile("D1", "4 fps graph", Color.FromArgb(40, 70, 140));
    d.SendNoWait(CMD_SET_BUTTONS, BuildZip(bgTiles, 2, Wide(0)));
    Thread.Sleep(800);
    d.SendSmall(2, 0, 0, 0);
    sends.Clear(); sw.Restart();
    for (int k = 1; k <= 80; k++) {
      sends.Add(d.SendTimed(CMD_PARTIAL, BuildZip(new Dictionary<int, byte[]>(), 2, Wide(k))));
      while (sw.ElapsedMilliseconds < k * 250) Thread.Sleep(1);
    }
    d.Log("D1 send-block per frame: " + Stats(sends) + "  (last frame sent = 80)");
    d.AllKeys("STOP", "last sent: 80", Color.FromArgb(180, 30, 30), 2);
    for (int s = 0; s < 6; s++) { d.SendSmall(2, 0, 0, 0); Thread.Sleep(1000); }

    // D2: small screen graph flat out, 60 frames, then STOP
    d.Log("D2: small screen 60 frames flat out");
    d.AllKeys("D2", "flat out graph", Color.FromArgb(40, 120, 70), 2);
    sends.Clear(); sw.Restart();
    for (int k = 1; k <= 60; k++)
      sends.Add(d.SendTimed(CMD_PARTIAL, BuildZip(new Dictionary<int, byte[]>(), 2, Wide(100 + k))));
    d.Log(string.Format("D2 {0} ms for 60 frames; send-block per frame: {1}  (last frame sent = 160)", sw.ElapsedMilliseconds, Stats(sends)));
    d.AllKeys("STOP", "last sent: 160", Color.FromArgb(180, 30, 30), 2);
    for (int s = 0; s < 8; s++) { d.SendSmall(2, 0, 0, 0); Thread.Sleep(1000); }
    d.AllKeys("done", "round 2", Color.FromArgb(60, 60, 60), 2);
    d.Log("round 2 done");
  }

  // ---------- the probe ----------
  public static void Run(string path, int listenSeconds) {
    var d = new D200Probe(path);
    d.Log("opened " + path);
    Thread.Sleep(500);
    d.Cpu();

    d.Log("brightness 80");
    d.SendSimple(CMD_BRIGHT, Encoding.UTF8.GetBytes("80"));
    Thread.Sleep(200);

    // 1. full layout: 13 numbered tiles, small window in STATS mode
    var tiles = new Dictionary<int, byte[]>();
    for (int i = 0; i < 13; i++) tiles[i] = Tile(i.ToString(), string.Format("col {0} row {1}", i % 5, i / 5), Hue(i, 13));
    var zip = BuildZip(tiles, 0);
    d.Log("FULL SET_BUTTONS  " + d.Timed(CMD_SET_BUTTONS, zip));
    d.SendSmall(0, d.Cpu(), Mem(), 0);
    Thread.Sleep(1500);

    d.verboseAck = false;
    // 2. partial update of one tile, repeated
    var times = new List<string>();
    for (int k = 1; k <= 10; k++) {
      var one = new Dictionary<int, byte[]>();
      one[0] = Tile(k.ToString(), "1-tile test", Color.FromArgb(200, 60, 60));
      times.Add(d.Timed(CMD_PARTIAL, BuildZip(one, 0)));
    }
    foreach (var s in times) d.Log("PARTIAL x1   " + s);

    // 3. partial update of all 13 tiles, repeated
    times.Clear();
    var swAll = Stopwatch.StartNew();
    for (int k = 1; k <= 5; k++) {
      var all = new Dictionary<int, byte[]>();
      for (int i = 0; i < 13; i++) all[i] = Tile((k * 100 + i).ToString(), "13-tile test", Hue(i + k, 13));
      times.Add(d.Timed(CMD_PARTIAL, BuildZip(all, 0)));
    }
    foreach (var s in times) d.Log("PARTIAL x13  " + s);
    d.Log(string.Format("13-tile loop incl. rendering: {0:F0} ms per frame", swAll.Elapsed.TotalMilliseconds / 5));

    // 4. back to a clean layout and listen for presses; each press repaints that tile
    for (int i = 0; i < 13; i++) tiles[i] = Tile(i.ToString(), "press me", Hue(i, 13));
    d.Log("RESET LAYOUT  " + d.Timed(CMD_SET_BUTTONS, BuildZip(tiles, 0)));
    d.Log(string.Format("LISTENING {0} s - press buttons on the dock", listenSeconds));
    var count = new int[13];
    var end = d.clock.ElapsedMilliseconds + listenSeconds * 1000L;
    long nextStats = 0;
    while (d.clock.ElapsedMilliseconds < end) {
      if (d.clock.ElapsedMilliseconds >= nextStats) { d.SendSmall(0, d.Cpu(), Mem(), 0); nextStats = d.clock.ElapsedMilliseconds + 1000; }
      int[] p = null;
      lock (d.presses) if (d.presses.Count > 0) p = d.presses.Dequeue();
      if (p != null && p[0] < 13) {
        count[p[0]]++;
        var one = new Dictionary<int, byte[]>();
        one[p[0]] = Tile(count[p[0]].ToString(), "presses", Color.FromArgb(30, 30, 30));
        var r = d.Timed(CMD_PARTIAL, BuildZip(one, 0));
        d.Log(string.Format("  repaint tile {0}: {1}  (press->ack {2} ms)", p[0], r, d.clock.ElapsedMilliseconds - p[1]));
      }
      Thread.Sleep(10);
    }
    d.Log("done, acks received: " + d.ackCount);
  }
}
