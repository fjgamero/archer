using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Threading;
using System.Net;
using System.Net.Http;
using System.IO;

namespace Archer
{
    public partial class Form1 : Form
    {
        HttpListener listener;
        private static readonly HttpClient client = new HttpClient();

        Thread t;
        Thread es;
        DateTime startScanTime;
        
        bool flag = true;

        Dictionary<string, string> config = new Dictionary<string, string>();

        string version = "1.0";

        string user = "UNDEFINED";
        string command = "/0xstop/";
        public Form1()
        {
            InitializeComponent();
            this.Resize += SetMinimizeState;
            notifyIcon1.Click += ToggleMinimizeState;
        }

        private void SetMinimizeState(object sender, EventArgs e)
        {
            bool isMinimized = this.WindowState == FormWindowState.Minimized;

            this.ShowInTaskbar = !isMinimized;
            notifyIcon1.Visible = isMinimized;
        }

        private void ToggleMinimizeState(object sender, EventArgs e)
        {
            bool isMinimized = this.WindowState == FormWindowState.Minimized;
            this.WindowState = (isMinimized) ? FormWindowState.Normal : FormWindowState.Minimized;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            this.WindowState = FormWindowState.Minimized;
            LoadConfiguration(config);
            listener = new HttpListener();
            //Llamada a web service que le devuelva los usuarios habilitados para agregarlos al listener
            LoadAuthorizedUsers(listener, config["AuthorizedUsersWS"]);
            /**/
            t = new Thread(() => ThreadProc(label1, label2, listener, ref flag, ref user, ref command, ref startScanTime, config));
            es = new Thread(() => ThreadScan(listener, ref flag, ref user, ref command, ref startScanTime, config, client, version, myFunction));
            t.Start();
            es.Start();
            /**/
        }

        private void LoadConfiguration(Dictionary<string, string> config)
        {
            try
            {
                StreamReader f = new StreamReader("archer.conf");
                using (f)
                {
                    string line = f.ReadLine();
                    while (line != null)
                    {
                        if (line.StartsWith("#"))
                        {
                            line = f.ReadLine();
                            continue;
                        }
                        string[] tmp = line.Split('=');
                        config.Add(tmp[0], tmp[1]);
                        line = f.ReadLine();
                    }
                }
                f.Close();
            }
            catch(Exception ex)
            {
                MessageBox.Show("El fichero de configuración no se ha podido cargar. " + ex.Message);
            }

            this.Text += " "+config["Integration"];
        }

        private void LoadAuthorizedUsers(HttpListener listener, string baseUrl)
        {
            string html = string.Empty;

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(baseUrl);

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream))
            {
                html = reader.ReadToEnd();
            }

            string[] fields = html.Split(';');
            for(int i = 0; i < fields.Length; i++)
            {
                listener.Prefixes.Add("http://localhost:3104/"+fields[i]+"/");
            }
        }

        public static void ThreadProc(Label label1, Label label2, HttpListener listener, ref bool flag, ref string user, ref string command, ref DateTime startScanTime, Dictionary<string, string> config)
        {
            if (!HttpListener.IsSupported)
            {
                MessageBox.Show("Windows XP SP2 or Server 2003 is required to use the HttpListener class.");
                return;
            }
            string port = config["Port"];
            // ORDERNES DEL PROTOCOLO **********************************************************************************************************************************************
            listener.Prefixes.Add("http://localhost:"+port+"/0xstart/");
            listener.Prefixes.Add("http://localhost:" + port + "/0xstop/");
            listener.Prefixes.Add("http://localhost:" + port + "/0xtime/");
            listener.Prefixes.Add("http://localhost:" + port + "/0xversion/");
            listener.Prefixes.Add("http://localhost:" + port + "/0xkill/");
            // *********************************************************************************************************************************************************************

            listener.Start();

            while (flag)
            {   
                HttpListenerContext context = listener.GetContext();
                HttpListenerRequest request = context.Request;

                if (!request.RawUrl.ToString().StartsWith("/0x")){
                    user = request.RawUrl.ToString();
                    label1.Text = "Usuario: " + user + "   ["+DateTime.Now+"]";
                }
                else
                {
                    command = request.RawUrl.ToString();
                    label2.Text = "Orden en ejecución: " + command + "   [" + DateTime.Now + "]";

                    if (command.Equals("/0xtime/"))
                    {
                        startScanTime = DateTime.Now;
                    }
                }
                
                HttpListenerResponse response = context.Response;
                string responseString = "<HTML><BODY> Hello world!</BODY></HTML>";
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
            }
        }

        public static void ThreadScan(HttpListener listener, ref bool flag, ref string user, ref string command, ref DateTime startScanTime, Dictionary<string, string> config, HttpClient client, string version, Func<string, string> myFunction)
        {
            while (flag)
            {
                if (command.Equals("/0xstart/"))
                {
                    try
                    {
                        string[] files = Directory.GetFiles(config["ScanPath"],"*.pdf");
                        for (int i = 0; i < files.Length; i++)
                        {
                            if (startScanTime > File.GetCreationTime(files[i])) continue;
                            string baseUrl = config["UploadWS"];
                            Dictionary<string, string> parameters = new Dictionary<string, string>();
                            string f = files[i].Substring(files[i].LastIndexOf("\\") + 1);
                            parameters.Add("filename", f.Substring(0, f.IndexOf(".")));
                            parameters.Add("extension", f.Substring(f.IndexOf(".") + 1));
                            parameters.Add("user", user);
                            MultipartFormDataContent form = new MultipartFormDataContent();
                            HttpContent content = new StringContent(files[i]);
                            HttpContent DictionaryItems = new FormUrlEncodedContent(parameters);
                            form.Add(content, "fileToUpload");
                            form.Add(DictionaryItems, "parameters");

                            var stream = new FileStream(files[i], FileMode.Open);
                            content = new StreamContent(stream);

                            content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("form-data")
                            {
                                Name = "fileToUpload",
                                FileName = "fichero.pdf"
                            };
                            form.Add(content);

                            HttpResponseMessage response = null;

                            try
                            {
                                response = (client.PostAsync(baseUrl, form)).Result;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex.Message);
                            }

                            var k = response.Content.ReadAsStringAsync().Result;

                            File.Move(files[i], config["ScannedPath"] +"\\" + f.Substring(0, f.IndexOf(".")) + "." + f.Substring(f.IndexOf(".") + 1));

                            break;
                        }
                    }
                    catch { }
                    Thread.Sleep(1000);
                }

                else if (command.Equals("/0xversion/"))
                {
                    try
                    {
                        string baseUrl = config["VersionWS"];
                        Dictionary<string, string> parameters = new Dictionary<string, string>();
                        parameters.Add("version", version);
                        parameters.Add("user", user);
                        MultipartFormDataContent form = new MultipartFormDataContent();
                        HttpContent DictionaryItems = new FormUrlEncodedContent(parameters);
                        form.Add(DictionaryItems, "parameters");
                    
                        HttpResponseMessage response = null;

                        try
                        {
                            response = (client.PostAsync(baseUrl, form)).Result;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                        }

                        var k = response.Content.ReadAsStringAsync().Result;
                    }
                    catch { }
                    Thread.Sleep(1000);
                }
                else if (command.Equals("/0xkill/"))
                {
                    flag = false;
                    MessageBox.Show(flag+"");
                }
            }
        }

        public string myFunction(string name)
        {
            return "Hello " + name;
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            //e.Cancel = true;

            /**/
            flag = false;
            try
            {
                listener.Stop();
            }catch(Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            /**/
        }

        private void Form1_Resize(object sender, EventArgs e)
        {
            if (FormWindowState.Minimized == this.WindowState)
            {
                notifyIcon1.Visible = true;
                this.Hide();
            }

            else if (FormWindowState.Normal == this.WindowState)
            {
                notifyIcon1.Visible = false;
            }
        }

        private void notifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            
        }

        private void notifyIcon1_MouseClick(object sender, MouseEventArgs e)
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
        }
    }
}
