using OpenCvSharp;
using SkiaSharp;
using System.Runtime.InteropServices;
using ViewFaceCore;
using ViewFaceCore.Core;
using ViewFaceCore.Model;
using Size = System.Drawing.Size;

namespace ViewFaceDemo;

public sealed class MainForm : Form
{
    private const float SamePersonThreshold = 0.6f;
    private const int CameraIndex = 0;

    private FaceDetector? _detector;
    private FaceLandmarker? _landmarker;
    private FaceRecognizer? _recognizer;
    private VideoCapture? _capture;
    private CancellationTokenSource? _cameraCts;
    private FaceCompareData[] _compareData = [];
    private List<(string Name, float Similarity)> _lastResults = [];
    private float[]? _lastFeature;
    private bool _realtimeMode;
    private readonly object _frameLock = new();

    private PictureBox _cameraBox = null!;
    private DataGridView _resultGrid = null!;
    private Label _statusLabel = null!;
    private Label _bestMatchLabel = null!;
    private Button _btnStartCamera = null!;
    private Button _btnStopCamera = null!;
    private Button _btnCapture = null!;
    private Button _btnRealtime = null!;
    private Button _btnLoadImages = null!;
    private Label _libInfoLabel = null!;

    public MainForm()
    {
        Text = "人脸实时比对系统";
        Size = new Size(1100, 680);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei", 9F);

        InitializeComponent();
        Shown += async (_, _) => await LoadCompareImagesAsync("test-images");
    }

    private void InitializeComponent()
    {
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63F));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        Controls.Add(mainLayout);

        // ---- 工具栏 ----
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 6, 6, 0),
            FlowDirection = FlowDirection.LeftToRight
        };
        mainLayout.SetColumnSpan(toolbar, 2);
        mainLayout.Controls.Add(toolbar, 0, 0);

        _btnStartCamera = new Button { Text = "Start Camera", BackColor = Color.FromArgb(0, 120, 215), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Height = 32, AutoSize = true, Cursor = Cursors.Hand, Font = new Font("Microsoft YaHei", 9F) };
        _btnStopCamera = new Button { Text = "Stop Camera", BackColor = Color.FromArgb(200, 60, 60), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Height = 32, AutoSize = true, Cursor = Cursors.Hand, Font = new Font("Microsoft YaHei", 9F), Enabled = false };
        _btnCapture = new Button { Text = "Capture", BackColor = Color.FromArgb(0, 150, 80), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Height = 32, AutoSize = true, Cursor = Cursors.Hand, Font = new Font("Microsoft YaHei", 9F), Enabled = false };
        _btnRealtime = new Button { Text = "Realtime OFF", BackColor = Color.FromArgb(80, 80, 80), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Height = 32, AutoSize = true, Cursor = Cursors.Hand, Font = new Font("Microsoft YaHei", 9F), Enabled = false };
        _btnLoadImages = new Button { Text = "Load Images", BackColor = Color.FromArgb(100, 70, 140), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Height = 32, AutoSize = true, Cursor = Cursors.Hand, Font = new Font("Microsoft YaHei", 9F) };

        _btnStartCamera.Click += StartCamera;
        _btnStopCamera.Click += StopCamera;
        _btnCapture.Click += CaptureAndCompare;
        _btnRealtime.Click += ToggleRealtime;
        _btnLoadImages.Click += async (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "选择比对图片目录" };
            if (dlg.ShowDialog() == DialogResult.OK)
                await LoadCompareImagesAsync(dlg.SelectedPath);
        };

        toolbar.Controls.AddRange(
            [_btnStartCamera, _btnStopCamera, _btnCapture,
             _btnRealtime, new Label { Text = "   ", Width = 20, AutoSize = false },
             _btnLoadImages]);

        // ---- 左侧：摄像头画面 ----
        var leftPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        mainLayout.Controls.Add(leftPanel, 0, 1);

        _cameraBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(30, 30, 30),
            SizeMode = PictureBoxSizeMode.Zoom
        };
        leftPanel.Controls.Add(_cameraBox);
        _cameraBox.Paint += (_, e) =>
        {
            if (_cameraBox.Image == null)
            {
                using var font = new Font("Microsoft YaHei", 14F);
                var text = "摄像头未启动";
                var sz = e.Graphics.MeasureString(text, font);
                e.Graphics.DrawString(text, font, Brushes.Gray,
                    (_cameraBox.Width - sz.Width) / 2,
                    (_cameraBox.Height - sz.Height) / 2);
            }
        };

        // ---- 右侧：比对结果 ----
        var rightPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 6, 6) };
        mainLayout.Controls.Add(rightPanel, 1, 1);

        _resultGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        _resultGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Rank",
            HeaderText = "排名",
            Width = 45,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        _resultGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Image",
            HeaderText = "图片",
            Width = 130
        });
        _resultGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Similarity",
            HeaderText = "相似度",
            Width = 70,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                Format = "0.0000"
            }
        });
        _resultGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Verdict",
            HeaderText = "判定",
            Width = 80,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        rightPanel.Controls.Add(_resultGrid);

        // ---- 底部状态栏 ----
        var statusPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 2, 6, 0),
            FlowDirection = FlowDirection.LeftToRight
        };
        mainLayout.SetColumnSpan(statusPanel, 2);
        mainLayout.Controls.Add(statusPanel, 0, 2);

        _statusLabel = new Label { Text = "就绪", AutoSize = true, ForeColor = Color.Gray };
        _libInfoLabel = new Label { Text = " | 比对库: 0 张", AutoSize = true, ForeColor = Color.Gray };
        _bestMatchLabel = new Label { Text = "", AutoSize = true, ForeColor = Color.DarkGreen, Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold) };
        statusPanel.Controls.AddRange([_statusLabel, _libInfoLabel, _bestMatchLabel]);
    }

    // ==================== 摄像头控制 ====================

    private async void StartCamera(object? sender, EventArgs e)
    {
        await Task.Run(() =>
        {
            InitFaceEngines();
            _capture = new VideoCapture(CameraIndex);
            if (!_capture.IsOpened())
            {
                BeginInvoke(() =>
                {
                    _statusLabel.Text = "摄像头打开失败！";
                    _statusLabel.ForeColor = Color.Red;
                });
                _capture.Dispose();
                _capture = null;
                return;
            }

            _cameraCts = new CancellationTokenSource();
            var token = _cameraCts.Token;

            BeginInvoke(() =>
            {
                _btnStartCamera.Enabled = false;
                _btnStopCamera.Enabled = true;
                _btnCapture.Enabled = true;
                _btnRealtime.Enabled = true;
                _statusLabel.Text = "摄像头运行中...";
                _statusLabel.ForeColor = Color.Green;
            });

            using var frame = new Mat();
            var lastProcess = DateTime.MinValue;

            while (!token.IsCancellationRequested)
            {
                _capture.Read(frame);
                if (frame.Empty() || IsDisposed) break;

                // 实时比对：每500ms检测一次
                if (_realtimeMode && (DateTime.Now - lastProcess).TotalMilliseconds > 500)
                {
                    lastProcess = DateTime.Now;
                    var feat = TryExtractFeature(frame);
                    if (feat != null)
                    {
                        lock (_frameLock)
                        {
                            _lastFeature = feat;
                            _lastResults = CompareAll(feat);
                        }
                        BeginInvoke(() => RefreshResultGrid());
                    }
                }

                // 显示画面
                var bitmap = MatToBitmap(frame);
                var oldImage = _cameraBox.Image;
                _cameraBox.Image = bitmap;
                oldImage?.Dispose();
            }
        });
    }

    private void StopCamera(object? sender, EventArgs e)
    {
        _cameraCts?.Cancel();
        _capture?.Dispose();
        _capture = null;

        _btnStartCamera.Enabled = true;
        _btnStopCamera.Enabled = false;
        _btnCapture.Enabled = false;
        _btnRealtime.Enabled = false;
        _statusLabel.Text = "摄像头已停止";
        _statusLabel.ForeColor = Color.Gray;
    }

    // ==================== 拍照比对 ====================

    private async void CaptureAndCompare(object? sender, EventArgs e)
    {
        if (_capture == null || !_capture.IsOpened()) return;

        _btnCapture.Enabled = false;
        _statusLabel.Text = "正在比对...";
        _statusLabel.ForeColor = Color.Orange;
        _bestMatchLabel.Text = "";

        await Task.Run(() =>
        {
            using var frame = new Mat();
            _capture.Read(frame);
            if (!frame.Empty())
            {
                var feat = TryExtractFeature(frame);
                if (feat != null)
                {
                    lock (_frameLock)
                    {
                        _lastFeature = feat;
                        _lastResults = CompareAll(feat);
                    }
                }
                else
                {
                    _lastResults.Clear();
                }
            }
        });

        BeginInvoke(() =>
        {
            if (_lastResults.Count > 0)
            {
                RefreshResultGrid();
                _statusLabel.Text = "比对完成";
                _statusLabel.ForeColor = Color.Green;
            }
            else
            {
                _resultGrid.Rows.Clear();
                _statusLabel.Text = "未检测到人脸，请调整位置";
                _statusLabel.ForeColor = Color.Red;
                _cameraBox.Invalidate();
            }
            _btnCapture.Enabled = true;
        });
    }

    // ==================== 实时比对切换 ====================

    private void ToggleRealtime(object? sender, EventArgs e)
    {
        _realtimeMode = !_realtimeMode;
        if (_realtimeMode)
        {
            _btnRealtime.Text = "\U0001F504 实时比对: 开";
            _btnRealtime.BackColor = Color.FromArgb(0, 150, 80);
            _btnCapture.Enabled = false;
            _statusLabel.Text = "实时比对模式 - 自动检测中...";
        }
        else
        {
            _btnRealtime.Text = "\U0001F504 实时比对: 关";
            _btnRealtime.BackColor = Color.FromArgb(80, 80, 80);
            _btnCapture.Enabled = true;
            _statusLabel.Text = "摄像头运行中...";
        }
        _statusLabel.ForeColor = Color.Green;
    }

    // ==================== 比对库加载（参数化） ====================

    private async Task LoadCompareImagesAsync(string dir)
    {
        if (!Directory.Exists(dir))
        {
            _libInfoLabel.Text = $" | 比对库: 目录不存在";
            return;
        }

        var files = Directory.GetFiles(dir)
            .Where(f =>
            {
                var ext = Path.GetExtension(f).ToLower();
                return ext is ".jpg" or ".jpeg" or ".png" or ".bmp";
            })
            .OrderBy(f => int.TryParse(Path.GetFileNameWithoutExtension(f), out var n) ? n : int.MaxValue)
            .ToList();

        if (files.Count == 0)
        {
            _libInfoLabel.Text = $" | 比对库: 无图片";
            return;
        }

        _statusLabel.Text = $"正在加载 {files.Count} 张比对图片...";
        _statusLabel.ForeColor = Color.Orange;
        _btnLoadImages.Enabled = false;

        var data = await Task.Run(() =>
        {
            InitFaceEngines();
            return PreExtractFeatures(files);
        });

        _compareData = data;
        int valid = _compareData.Count(d => d.HasFeature);
        _libInfoLabel.Text = $" | 比对库: {valid}/{data.Length} 张已注册";
        _statusLabel.Text = "就绪";
        _statusLabel.ForeColor = Color.Gray;
        _btnLoadImages.Enabled = true;

        // 填充表格空行
        RefreshResultGrid();
    }

    // ==================== 核心比对引擎 ====================

    private void InitFaceEngines()
    {
        _detector ??= new FaceDetector();
        _landmarker ??= new FaceLandmarker();
        _recognizer ??= new FaceRecognizer();
    }

    private FaceCompareData[] PreExtractFeatures(List<string> files)
    {
        var entries = new FaceCompareData[files.Count];
        for (int i = 0; i < files.Count; i++)
        {
            string name = Path.GetFileName(files[i]);
            try
            {
                float[] feat = ExtractFeature(files[i]);
                entries[i] = new FaceCompareData(name, feat, files[i]);
            }
            catch
            {
                entries[i] = new FaceCompareData(name, null, files[i]);
            }
        }
        return entries;
    }

    private float[] ExtractFeature(string path)
    {
        using var fs = File.OpenRead(path);
        using var bmp = SKBitmap.Decode(fs)
            ?? throw new Exception("图片无法解码");
        FaceImage image = bmp.ToFaceImage();

        var faces = _detector!.Detect(image);
        if (faces.Length == 0)
            throw new Exception("未检测到人脸");

        var marks = _landmarker!.Mark(image, faces[0]);
        return _recognizer!.Extract(image, marks);
    }

    private float[]? TryExtractFeature(Mat frame)
    {
        try
        {
            using var rgba = new Mat();
            Cv2.CvtColor(frame, rgba, ColorConversionCodes.BGR2RGBA);

            var info = new SKImageInfo(rgba.Width, rgba.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var bmp = new SKBitmap(info);
            bmp.SetPixels(rgba.Data);

            FaceImage image = bmp.ToFaceImage();
            var faces = _detector!.Detect(image);
            if (faces.Length == 0) return null;

            var marks = _landmarker!.Mark(image, faces[0]);
            return _recognizer!.Extract(image, marks);
        }
        catch
        {
            return null;
        }
    }

    private List<(string Name, float Similarity)> CompareAll(float[] feature)
    {
        var results = new List<(string Name, float Similarity)>();
        foreach (var entry in _compareData)
        {
            if (entry.Feature == null) continue;
            float sim = _recognizer!.Compare(feature, entry.Feature);
            results.Add((entry.Name, sim));
        }
        results.Sort((a, b) => b.Similarity.CompareTo(a.Similarity));
        return results;
    }

    // ==================== UI 更新 ====================

    private void RefreshResultGrid()
    {
        _resultGrid.Rows.Clear();

        if (_lastResults.Count == 0)
        {
            foreach (var entry in _compareData)
            {
                _resultGrid.Rows.Add("-", entry.Name, "-", entry.HasFeature ? "待比对" : "无特征");
            }
            _bestMatchLabel.Text = "";
            return;
        }

        int rank = 0;
        foreach (var (name, sim) in _lastResults)
        {
            rank++;
            bool same = sim >= SamePersonThreshold;
            int rowIdx = _resultGrid.Rows.Add(rank.ToString(), name, sim, same ? "\u2605 同一人" : "不同人");
            _resultGrid.Rows[rowIdx].DefaultCellStyle.ForeColor = same ? Color.DarkGreen : Color.Black;
            _resultGrid.Rows[rowIdx].DefaultCellStyle.Font = same
                ? new Font("Microsoft YaHei", 9F, FontStyle.Bold)
                : new Font("Microsoft YaHei", 9F);
        }

        var best = _lastResults[0];
        if (best.Similarity >= SamePersonThreshold)
        {
            _bestMatchLabel.Text = $" | \u2605 最佳匹配: {best.Name} ({best.Similarity:F4})";
            _bestMatchLabel.ForeColor = Color.DarkGreen;
        }
        else
        {
            _bestMatchLabel.Text = $" | 未匹配 (最高: {best.Name} {best.Similarity:F4})";
            _bestMatchLabel.ForeColor = Color.DarkOrange;
        }
    }

    // ==================== OpenCV Mat -> WinForms Bitmap ====================

    private static Bitmap MatToBitmap(Mat mat)
    {
        using var rgb = new Mat();
        Cv2.CvtColor(mat, rgb, ColorConversionCodes.BGR2RGB);

        var bitmap = new Bitmap(rgb.Width, rgb.Height,
            System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        var rect = new Rectangle(0, 0, rgb.Width, rgb.Height);
        var bmpData = bitmap.LockBits(rect,
            System.Drawing.Imaging.ImageLockMode.WriteOnly,
            bitmap.PixelFormat);

        int rowBytes = rgb.Width * 3;
        byte[] row = new byte[rowBytes];

        for (int y = 0; y < rgb.Height; y++)
        {
            Marshal.Copy(rgb.Data + y * (int)rgb.Step(), row, 0, rowBytes);
            Marshal.Copy(row, 0, bmpData.Scan0 + y * bmpData.Stride, rowBytes);
        }

        bitmap.UnlockBits(bmpData);
        return bitmap;
    }

    // ==================== 清理 ====================

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cameraCts?.Cancel();
        _capture?.Dispose();
        _cameraBox.Image?.Dispose();
        _detector?.Dispose();
        _landmarker?.Dispose();
        _recognizer?.Dispose();
        base.OnFormClosing(e);
    }
}

sealed class FaceCompareData
{
    public string Name { get; }
    public float[]? Feature { get; }
    public string FilePath { get; }
    public bool HasFeature => Feature != null;

    public FaceCompareData(string name, float[]? feature, string filePath)
    {
        Name = name;
        Feature = feature;
        FilePath = filePath;
    }
}