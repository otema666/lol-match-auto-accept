using MaterialSkin;
using MaterialSkin.Controls;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Net;
using System.Reflection;
using Arena_lol.Properties;

namespace LoLAutoWinForms
{
    public partial class Form1 : MaterialForm
    {
        // Mapa de campeones y colas
        private readonly Dictionary<string, int> CHAMPION_IDS = new Dictionary<string, int>
        {
            {"Swain", 50}, {"Tryndamere", 23}
        };
        private readonly Dictionary<int, string> QUEUE_TYPES = new Dictionary<int, string>
        {
            {420, "Ranked Solo/Duo"},
            {440, "Ranked Flex"},
            {450, "ARAM"},
            {400, "Normal Draft"},
            {430, "Normal Blind"},
            {490, "Normal Quickplay"},
            {830, "Co-op vs AI Intro"},
            {840, "Co-op vs AI Beginner"},
            {850, "Co-op vs AI Intermediate"},
            {1700, "Arena"},
            {1090, "Teamfight Tactics"},
            {1130, "Teamfight Tactics Ranked"}
        };

        // Controles UI
        private Label lblQueueStatus, lblGameMode, lblConnection;
        private CheckBox chkAutoAccept;
        private TextBox txtLog;
        private System.Windows.Forms.Timer pollTimer;

        // HTTP & autenticación
        private HttpClient httpClient;
        private string baseUrl, authHeader;
        private bool isRunning = false;
        private string selectedChampion = null;

        private Button btnStartDeceive;
        private string deceiveExePath = null;

        public Form1()
        {
            this.Icon = Resources.app;
            // Inicializa el tema MaterialSkin
            var materialSkinManager = MaterialSkinManager.Instance;
            materialSkinManager.AddFormToManage(this);
            materialSkinManager.Theme = MaterialSkinManager.Themes.DARK;
            materialSkinManager.ColorScheme = new ColorScheme(
                Primary.BlueGrey800, Primary.BlueGrey900,
                Primary.BlueGrey500, Accent.LightBlue200, TextShade.WHITE);

            Text = "LoL Auto-Ban & Accept";
            Width = 500; Height = 700;
            InitUI();
            DetectOrShowDeceiveButton();

            // Intentar conectar automáticamente al cliente al abrir la app
            Shown += async (s, e) =>
            {
                await ConnectToClient();
            };
        }

        private void InitUI()
        {
            // TableLayoutPanel principal
            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 5,
                ColumnCount = 1,
                Padding = new Padding(10),
                BackColor = System.Drawing.Color.FromArgb(38, 50, 56), // Fondo oscuro
                RowStyles =
        {
            new RowStyle(SizeType.Absolute, 100),
            new RowStyle(SizeType.Absolute, 40),
            new RowStyle(SizeType.Absolute, 80),
            new RowStyle(SizeType.Absolute, 50),
            new RowStyle(SizeType.Percent, 100)
        }
            };
            Controls.Add(mainLayout);

            // Status GroupBox
            var grpStatus = new MaterialCard { Dock = DockStyle.Fill, Padding = new Padding(10) };
            var statusLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 3, BackColor = System.Drawing.Color.Transparent };
            lblQueueStatus = new MaterialLabel { Text = "No conectado", ForeColor = System.Drawing.Color.OrangeRed, TextAlign = System.Drawing.ContentAlignment.MiddleCenter, Dock = DockStyle.Fill };
            lblGameMode = new MaterialLabel { Text = "Desconocido", TextAlign = System.Drawing.ContentAlignment.MiddleCenter, Dock = DockStyle.Fill };
            lblConnection = new MaterialLabel { Text = "✗ Desconectado", ForeColor = System.Drawing.Color.OrangeRed, TextAlign = System.Drawing.ContentAlignment.MiddleCenter, Dock = DockStyle.Fill };
            statusLayout.Controls.Add(lblQueueStatus, 0, 0);
            statusLayout.Controls.Add(lblGameMode, 1, 0);
            statusLayout.Controls.Add(lblConnection, 2, 0);
            grpStatus.Controls.Add(statusLayout);
            mainLayout.Controls.Add(grpStatus, 0, 0);

            // Auto-Accept
            chkAutoAccept = new MaterialCheckbox { Text = "Auto acept", Checked = true, Dock = DockStyle.Left, AutoSize = true };
            mainLayout.Controls.Add(chkAutoAccept, 0, 1);

            // Champion Selection
            var grpBan = new MaterialCard { Dock = DockStyle.Fill, Padding = new Padding(10) };
            var banLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, BackColor = System.Drawing.Color.Transparent };
            var lblChamp = new MaterialLabel { Text = "Ban:", AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            var cmbChampions = new MaterialComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
            cmbChampions.Items.AddRange(CHAMPION_IDS.Keys.ToArray());
            cmbChampions.SelectedIndexChanged += (s, e) =>
            {
                selectedChampion = cmbChampions.SelectedItem as string;
                Log($"Campeón seleccionado: {selectedChampion}");
            };
            banLayout.Controls.Add(lblChamp);
            banLayout.Controls.Add(cmbChampions);
            grpBan.Controls.Add(banLayout);
            mainLayout.Controls.Add(grpBan, 0, 2);

            // Botón para iniciar/reiniciar Deceive
            var pnlButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, BackColor = System.Drawing.Color.Transparent };
            btnStartDeceive = new MaterialButton
            {
                Text = "Iniciar Deceive",
                Width = 200,
                Visible = true
            };
            btnStartDeceive.Click += async (s, e) => await StartDeceive();
            pnlButtons.Controls.Add(btnStartDeceive);
            mainLayout.Controls.Add(pnlButtons, 0, 3);

            // Log
            txtLog = new TextBox
            {
                Multiline = true,
                Dock = DockStyle.Fill,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = System.Drawing.Color.FromArgb(33, 33, 33),
                ForeColor = System.Drawing.Color.White
            };
            mainLayout.Controls.Add(txtLog, 0, 4);

            // Timer para polling
            pollTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            pollTimer.Tick += async (s, e) => await PollStatusAndAct();

            // Inicia la automatización siempre
            isRunning = true;
            pollTimer.Start();
        }

        private void Log(string msg)
        {
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
        }

        private async Task<bool> ConnectToClient()
        {
            try
            {
                // 1) Leer lockfile
                int port;
                string token;
                FindAndReadLockfile(out port, out token);
                baseUrl = $"https://127.0.0.1:{port}";
                authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{token}"));


                // 2) Crear HttpClient ignorando SSL
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (HttpRequestMessage req, X509Certificate2 cert,
                        X509Chain chain, SslPolicyErrors errors) => true
                };
                httpClient = new HttpClient(handler);
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authHeader);
                httpClient.Timeout = TimeSpan.FromSeconds(3);

                Log("Connected to League Client");
                UpdateStatus("Connected", "Ready", "✓ Connected");
                //btnStartDeceive.Visible = false;
                return true;
            }
            catch (Exception ex)
            {
                Log($"Connection error: {ex.Message}");
                UpdateStatus("Connection Error", "Unknown", "✗ Disconnected");
                DetectOrShowDeceiveButton();
                return false;
            }
        }

        private void UpdateStatus(string queue, string mode, string conn)
        {
            lblQueueStatus.Text = queue;
            lblGameMode.Text = mode;
            lblConnection.Text = conn;
        }

        private void FindAndReadLockfile(out int port, out string token)
        {
            var proc = Process.GetProcessesByName("LeagueClientUx").FirstOrDefault()
                       ?? throw new Exception("LeagueClientUx.exe no está en ejecución");

            string dir = Path.GetDirectoryName(proc.MainModule.FileName);
            string lockfile = Path.Combine(dir, "lockfile");
            if (!File.Exists(lockfile))
                throw new Exception("lockfile no encontrado en " + dir);

            string content;
            // Lee el archivo permitiendo acceso compartido
            using (var fs = new FileStream(lockfile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs))
            {
                content = reader.ReadToEnd();
            }

            var parts = content.Split(':');
            if (parts.Length < 5)
                throw new Exception("Formato inesperado de lockfile");

            port = int.Parse(parts[2]);
            token = parts[3];
        }


        private async Task<string> GetAsync(string path)
        {
            var resp = await httpClient.GetAsync(baseUrl + path);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadAsStringAsync() : null;
        }

        private async Task<bool> PostAsync(string path)
        {
            var resp = await httpClient.PostAsync(baseUrl + path, null);
            return resp.IsSuccessStatusCode;
        }

        private async Task<bool> PatchAsync(string path, string json)
        {
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await httpClient.PatchAsync(baseUrl + path, content);
            return resp.IsSuccessStatusCode;
        }

        private async Task PollStatusAndAct()
        {
            if (!isRunning) return;

            try
            {
                // 1) Estado de matchmaking
                var search = await GetAsync("/lol-matchmaking/v1/search");
                if (search != null)
                {
                    var job = JObject.Parse(search);
                    if (job["searchState"]?.ToString() == "Searching")
                    {
                        int qid = job["queueId"].Value<int>();
                        int tInQ = job["timeInQueue"].Value<int>();
                        UpdateStatus($"In Queue ({tInQ}s)", QUEUE_TYPES.ContainsKey(qid) ? QUEUE_TYPES[qid] : $"Unknown ({qid})", "✓ Connected");
                    }
                }

                // 2) Ready‐check + auto‐accept
                var rc = await GetAsync("/lol-matchmaking/v1/ready-check");
                if (rc != null && JObject.Parse(rc)["state"]?.ToString() == "InProgress")
                {
                    UpdateStatus("Ready Check", "Match Found", "✓ Connected");
                    if (chkAutoAccept.Checked)
                    {
                        Log("Ready-check detected. Accepting...");
                        await PostAsync("/lol-matchmaking/v1/ready-check/accept");
                        Log("✓ Ready-check accepted!");
                    }
                }

                // 3) Auto-ban
                if (!string.IsNullOrEmpty(selectedChampion))
                {
                    var sess = await GetAsync("/lol-champ-select/v1/session");
                    if (sess != null)
                    {
                        var job = JObject.Parse(sess);
                        int myCell = job["localPlayerCellId"].Value<int>();
                        int banId = CHAMPION_IDS[selectedChampion];
                        foreach (var grp in job["actions"])
                        {
                            foreach (var act in grp)
                            {
                                if (act["actorCellId"].Value<int>() == myCell
                                    && act["type"].ToString() == "ban"
                                    && act["isInProgress"].Value<bool>())
                                {
                                    Log($"Banning {selectedChampion}...");
                                    int actionId = act["id"].Value<int>();
                                    var payload = new JObject { ["championId"] = banId, ["completed"] = true };
                                    if (await PatchAsync($"/lol-champ-select/v1/session/actions/{actionId}", payload.ToString()))
                                        Log($"✓ {selectedChampion} banned successfully!");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error in automation: {ex.Message}");
                UpdateStatus("Error", "Unknown", "✗ Disconnected");
            }
        }

        private async Task StartAutomation()
        {
            if (string.IsNullOrEmpty(selectedChampion))
            {
                MessageBox.Show("Por favor selecciona un campeón para banear.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }


            if (!await ConnectToClient())
            {
                return;
            }

            isRunning = true;
            pollTimer.Start();
            Log("Automation started!");
        }

        private void StopAutomation()
        {
            isRunning = false;
            pollTimer.Stop();
            UpdateStatus("Not Connected", "Idle", "✗ Disconnected");
            Log("Automation stopped!");
        }

        private void DetectOrShowDeceiveButton()
        {
            // El botón siempre es visible
            btnStartDeceive.Visible = true;

            if (IsLeagueClientRunning())
            {
                btnStartDeceive.Text = "Reiniciar LoL";
            }
            else
            {
                btnStartDeceive.Text = "Iniciar LoL";
                // Si no se ha encontrado la ruta, buscar o descargar
                Task.Run(async () =>
                {
                    deceiveExePath = await FindDeceiveExe();
                    if (deceiveExePath == null)
                    {
                        deceiveExePath = await DownloadDeceiveExe();
                    }
                });
            }
        }

        private bool IsLeagueClientRunning()
        {
            return Process.GetProcessesByName("LeagueClientUx").Any();
        }

        private async Task<string> FindDeceiveExe()
        {
            string userFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] searchFolders = new[]
            {
                userFolder,
                Path.Combine(userFolder, "Downloads"),
                Path.Combine(userFolder, "Desktop")
            };

            foreach (var folder in searchFolders)
            {
                try
                {
                    var files = Directory.GetFiles(folder, "Deceive.exe", SearchOption.TopDirectoryOnly);
                    if (files.Length > 0)
                        return files[0];
                }
                catch { /* Ignorar carpetas inaccesibles */ }
            }
            return null;
        }

        private async Task<string> DownloadDeceiveExe()
        {
            string userFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string destPath = Path.Combine(userFolder, "Deceive.exe");
            string url = "https://github.com/molenzwiebel/Deceive/releases/download/v1.14.0/Deceive.exe";
            try
            {
                using (var client = new WebClient())
                {
                    await client.DownloadFileTaskAsync(url, destPath);
                }
                return destPath;
            }
            catch (Exception ex)
            {
                Invoke((Action)(() => Log("Error descargando Deceive.exe: " + ex.Message)));
                return null;
            }
        }

        private async Task StartDeceive()
        {
            if (string.IsNullOrEmpty(deceiveExePath) || !File.Exists(deceiveExePath))
            {
                deceiveExePath = await DownloadDeceiveExe();
                if (deceiveExePath == null)
                {
                    MessageBox.Show("No se pudo encontrar ni descargar Deceive.exe.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            try
            {
                Process.Start(deceiveExePath);
                Log("Deceive.exe iniciado. Esperando al cliente de LoL...");
                // Espera unos segundos y vuelve a comprobar
                await Task.Delay(5000);
                DetectOrShowDeceiveButton();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al iniciar Deceive.exe: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
