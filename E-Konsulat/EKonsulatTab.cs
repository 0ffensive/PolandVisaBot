﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PolandVisaAuto;
using mshtml;
using pvhelper;

namespace E_Konsulat
{
    public class EKonsulatTab : IOleClientSite, IServiceProvider, IAuthenticate
    {
        #region Proxy def

        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int lpdwBufferLength);
        private Guid IID_IAuthenticate = new Guid("79eac9d0-baf9-11ce-8c82-00aa004ba90b");
        private const int INET_E_DEFAULT_ACTION = unchecked((int)0x800C0011);
        private const int S_OK = unchecked((int)0x00000000);
        private const int INTERNET_OPTION_PROXY = 38;
        private const int INTERNET_OPEN_TYPE_DIRECT = 1;
        private const int INTERNET_OPEN_TYPE_PROXY = 3;
        private string _currentUsername;
        private string _currentPassword;

        #endregion

        private WebBrowser webBrowser;
        private RichTextBox richText;
        private Button deleteTask;
        private Button renewTask;
        private bool _allowStep = true;
        private RotEvents _enum = RotEvents.Start;
        private KonsulatTask _currentTask = null;
        public List<KonsulatTask> Tasks = new List<KonsulatTask>();
        public bool IsTabExists { get; set; }
        private TabPage _tabPage;
        private KonsulatComparer vc = new KonsulatComparer();
        //private static DateTime _lastProxyDateTime = DateTime.Now;

        public delegate void TaskDelegate(KonsulatTask task);
        public event TaskDelegate TaskEvent;

        public delegate void TabDelegate(TabPage tab);
        public event TabDelegate TabEvent;

        public delegate void TabDelegateMain(EKonsulatTab visa, bool add);
        public event TabDelegateMain VisaEvent;

        public delegate void TabDelegateEx(TabPage tab, bool alarm);
        public event TabDelegateEx TabEventEx;

        public delegate void GetNextProxyDelegate();
        public event GetNextProxyDelegate GetNextProxyEvent;

        private SoundPlayer sp;
        private bool _playSound = false;
        int _countAttempt = 5;
        //private bool _blockAlert;

        private void initSound()
        {
            Assembly assembly;
            assembly = Assembly.GetExecutingAssembly();
            sp = new SoundPlayer(assembly.GetManifestResourceStream("PolandVisaAuto.WindowsError.wav"));
        }

        public EKonsulatTab(){}
        public EKonsulatTab(KonsulatTask task, TabPage tabPage)
        {
            initSound();
            Tasks.Add(task);
            _tabPage = tabPage;

            richText = (RichTextBox)_tabPage.Controls.Find("richText", true)[0];
            webBrowser = (WebBrowser)_tabPage.Controls.Find("webBrowser" + task.City, true)[0];
            webBrowser.ScriptErrorsSuppressed = true;
           // webBrowser.ProgressChanged += new WebBrowserProgressChangedEventHandler(webBrowser_ProgressChanged);
            webBrowser.DocumentCompleted += new WebBrowserDocumentCompletedEventHandler(webBrowser_DocumentCompleted);

            renewTask = (Button) _tabPage.Controls.Find("renewTask", true)[0];
            renewTask.Click += new System.EventHandler(this.renewTask_click);

            deleteTask = (Button)_tabPage.Controls.Find("deleteTask", true)[0];
            deleteTask.Click += new System.EventHandler(this.deleteTask_click);
            _changeProxyOnce = ImageResolver.Instance.UseProxy;
            SetProxy();
        }

        public void SetProxy()
        {
            if (ImageResolver.Instance.UseProxy)
            {

                var proxy = ImageResolver.Instance.GetCorrectCurrentWebProxy;
                if (proxy == null)
                {
                    SetProxyServer(null);
                }
                else
                {
                    object obj = webBrowser.ActiveXInstance;
                    IOleObject oc = obj as IOleObject;
                    oc.SetClientSite(this as IOleClientSite);

                    _currentUsername = ImageResolver.Instance.ProxyUsers[proxy].UserName;
                    _currentPassword = ImageResolver.Instance.ProxyUsers[proxy].Password;
                    SetProxyServer(proxy.Address.ToString());

                    webBrowser.Navigate("about:blank");
                    Application.DoEvents();
                }
            }
            else
            {
                SetProxyServer(null);
            }
        }

        private void deleteTask_click(object sender, EventArgs e)
        {
            if (sender as RegCount == null)
            {
                if (ImageResolver.Instance.ChekOnLimitRegistraions)
                {
                    if (GetNextProxyEvent != null)
                    {
                        GetNextProxyEvent();
                        SetProxy();
                    }
                }
            }
            if (VisaEvent != null)
                VisaEvent(this, false);

            Tasks.Remove(_currentTask);
            if (TaskEvent != null)
            {
                TaskEvent(_currentTask);
                _currentTask = null;
            }
            _enum = RotEvents.Start;

            if (Tasks.Count == 0 && TabEvent != null)
            {
                TabEvent(_tabPage);
            }

            _allowStep = true;
        }

        private void renewTask_click(object sender, EventArgs e)
        {
            TurnAlarmOn(false);
            if (VisaEvent != null)
                VisaEvent(this, false);
            _currentTask = null;
            _enum = RotEvents.Start;
            _allowStep = true;
        }

//        private void webBrowser_ProgressChanged(object sender, WebBrowserProgressChangedEventArgs e)
//        {
//            if (_blockAlert && webBrowser.ReadyState == WebBrowserReadyState.Complete)
//            {
//                HtmlElement head = webBrowser.Document.GetElementsByTagName("head")[0];
//                HtmlElement scriptEl = webBrowser.Document.CreateElement("script");
//                IHTMLScriptElement element = (IHTMLScriptElement)scriptEl.DomElement;
//                string alertBlocker = "window.alert = function () { }";
//                element.text = alertBlocker;
//                head.AppendChild(scriptEl);
//            }
//        }

        void webBrowser_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            _allowStep = true;
        }

        private string GetProxyInfo()
        {
            return ImageResolver.Instance.UseProxy
                ? "Прокси в работе!\r\nАдрес: " + ImageResolver.Instance.CurrentWebProxy.Address + "\r\n" + ImageResolver.Instance.CurrentWebProxyRegCount + "\r\n\r\n"
                : "";
        }

        private bool _changeProxyOnce = false;
        public void DoStep()
        {
            if (_playSound)
                playSound();

            if(!_allowStep)
                return;
            _allowStep = false;

            if (_changeProxyOnce != ImageResolver.Instance.UseProxy)
            {
                _changeProxyOnce = ImageResolver.Instance.UseProxy;
                SetProxy();
            }

            if (webBrowser.DocumentTitle == "Service Unavailable"
            || webBrowser.DocumentTitle == "Navigation Canceled"
            || webBrowser.DocumentTitle == "The proxy server isn't responding"
            || webBrowser.DocumentTitle == "Internet Explorer cannot display the webpage"
            || webBrowser.DocumentTitle == "This page can’t be displayed")
            {
                _allowStep = true;
                _enum = RotEvents.Start;
                if (ImageResolver.Instance.UseProxy)
                {
                    //if ((DateTime.Now - _lastProxyDateTime).Seconds > 15)
                    //{
                    //    _lastProxyDateTime = DateTime.Now;
                        if (GetNextProxyEvent != null)
                        {
                            GetNextProxyEvent();
                            SetProxy();
                        }
                    //}
                }
            }

            try
            {
                if(_tabPage != null && _currentTask != null)
                    _tabPage.ToolTipText = GetProxyInfo() +_currentTask.GetInfo();

                switch (_enum)
                {
                    case RotEvents.Start:
                        {
                            _countAttempt = 5;
                            Tasks.Sort(vc);
                            _currentTask = Tasks[0];
                            _tabPage.ToolTipText = GetProxyInfo() + _currentTask.GetInfo();
                            _enum = RotEvents.Firsthl;
                            webBrowser.Navigate(Const.UrlEkonsulat);
                            break;
                        }
                    case RotEvents.Firsthl:
                        {
                            SelectFromCombo("ctl00$ddlWersjeJezykowe", "Русская");
                            _allowStep = true;

//                            for (int i = 0; i < this.webBrowser.Document.GetElementById("ctl00$ddlWersjeJezykowe").Children.Count; i++)
//                            {
//                                HtmlElement child = this.webBrowser.Document.GetElementById("ctl00$ddlWersjeJezykowe").Children[i];
//                                if (child.InnerText == "Русская")
//                                {
//                                    this.webBrowser.Document.GetElementById("ctl00$ddlWersjeJezykowe").SetAttribute("selectedIndex", i.ToString());
//                                    break;
//                                }
//                            }
                            //webBrowser.Document.GetElementById("ctl00$ddlWersjeJezykowe").SetAttribute("selectedIndex", "17");
//                            webBrowser.Document.GetElementById("ctl00$tresc$cbListaKrajow").SetAttribute("selectedIndex", "4");
//                            webBrowser.Document.GetElementById("ctl00$tresc$cbListaPlacowek").SetAttribute("selectedIndex", "88");
                            _enum = RotEvents.FirstCombo;
                            break;
                        }
                    case RotEvents.FirstCombo:
                        {
                            SelectFromCombo("ctl00$tresc$cbListaKrajow", "УКРАИНА");
                            this.webBrowser.Document.GetElementById("ctl00$tresc$cbListaKrajow").InvokeMember("onchange");
                            _allowStep = true;

//                            webBrowser.Document.GetElementById("ctl00_plhMain_cboVAC").SetAttribute("selectedIndex", _currentTask.CityCode);
//                            webBrowser.Document.GetElementById("ctl00_plhMain_cboPurpose").SetAttribute("selectedIndex", _currentTask.PurposeCode);
                            _enum = RotEvents.SecondCombo;
//                            webBrowser.Document.GetElementById("ctl00_plhMain_btnSubmit").InvokeMember("click");
                            break;
                        }
                    case RotEvents.SecondCombo:
                        {
                            if (this.webBrowser.Document.GetElementById("ctl00$tresc$cbListaPlacowek").Children.Count == 0)
                            {
                                _allowStep = true;
                                return;
                            }
                            SelectFromCombo("ctl00$tresc$cbListaPlacowek", "Винница");
                            this.webBrowser.Document.GetElementById("ctl00$tresc$cbListaPlacowek").InvokeMember("onchange");
                            _allowStep = true;
//                            webBrowser.Document.GetElementById("ctl00_plhMain_tbxNumOfApplicants").SetAttribute("value", _currentTask.CountAdult.ToString());
//                            if (webBrowser.Document.GetElementById("ctl00_plhMain_txtChildren") != null)
//                                webBrowser.Document.GetElementById("ctl00_plhMain_txtChildren").SetAttribute("value", _currentTask.CountChild.ToString());
//
//                            for (int i = 0; i < this.webBrowser.Document.GetElementById("ctl00_plhMain_cboVisaCategory").Children.Count; i++)
//                            {
//                                HtmlElement child = this.webBrowser.Document.GetElementById("ctl00_plhMain_cboVisaCategory").Children[i];
//                                if (child.InnerText == _currentTask.Category)
//                                {
//                                    this.webBrowser.Document.GetElementById("ctl00_plhMain_cboVisaCategory").SetAttribute("selectedIndex", i.ToString());
//                                    break;
//                                }
//                            }
                            _enum = RotEvents.Submit;
//                            this.webBrowser.Document.GetElementById("ctl00_plhMain_cboVisaCategory").InvokeMember("onchange");
                            break;
                        }
                    case RotEvents.Submit:
                        {
//                            string showStopper = webBrowser.Document.GetElementById("ctl00_plhMain_lblMsg").InnerText;
//                            if (showStopper.Contains("Number of Persons should not be"))
//                            {
//                                Logger.Warning("Сбой в работе сайта. Ошибка: " + showStopper);
//                                _currentTask = null;
//                                _enum = RotEvents.Start;
//                                _allowStep = true;
//                                break;
//                            }
//                            richText.Text = "Свободна дата: " + showStopper;
//                            Logger.Info(_currentTask.City + ": "+ richText.Text);
//
//                            _tabPage.Text = _currentTask.CityV + "~" + (showStopper.Contains("No date(s) available") ? "No date(s)" : showStopper);
//                            if (!showStopper.Contains("No date(s) available"))
//                            {
//                                var apointmentDate = ProcessDate(showStopper);
//                                if (apointmentDate > _currentTask.GreenLineDt && apointmentDate < _currentTask.RedLineDt)
//                                {
//                                    webBrowser.Document.GetElementById("ctl00_plhMain_btnSubmit").InvokeMember("click");
//                                    _enum = RotEvents.FillReceipt;
//                                    TurnAlarmOn(true);
//                                    if (VisaEvent != null)
//                                        VisaEvent(this, true);
//                                    break;
//                                }
//                                else
//                                {
//                                    Logger.Info("Задание не укладывается в интервал разрешенных дат.");
//                                    Logger.Info(_currentTask.GetInfo());
//                                    _currentTask = null;
//                                    Tasks.Sort(vc);
//                                    foreach (VisaTask visaTask in Tasks)
//                                    {
//                                        if (apointmentDate > visaTask.GreenLineDt && apointmentDate < visaTask.RedLineDt)
//                                        {
//                                            _currentTask = visaTask;
//                                            _tabPage.ToolTipText = GetProxyInfo() + _currentTask.GetInfo();
//                                        }
//                                    }
//                                    if (_currentTask == null)
//                                    {
//                                        Logger.Warning("Нет заданий для даты " + showStopper);
//                                        throw new Exception("бегаем по кругу, ждем с моря погоды");
//                                    }
//                                    Logger.Info("Выбрали новое Задание");
//                                    Logger.Info(_currentTask.GetInfo());
//                                }
//                            }
//                            _enum = RotEvents.FirstCombo;
//                            webBrowser.Document.GetElementById("ctl00_plhMain_btnCancel").InvokeMember("click");
                            break;
                        }
//                    case RotEvents.FillReceipt:
//                        {
//                            Logger.Info(_currentTask.City+": Номер квитанции: " + _currentTask.Receipt);
//                            webBrowser.Document.GetElementById("ctl00_plhMain_repAppReceiptDetails_ctl01_txtReceiptNumber").SetAttribute("value", _currentTask.Receipt);
//                            _enum = RotEvents.FillEmail;
//                            webBrowser.Document.GetElementById("ctl00_plhMain_btnSubmit").InvokeMember("click");
//                            break;
//                        }
//                    case RotEvents.FillEmail:
//                        {
//                            if (webBrowser.Document.GetElementById("ctl00_plhMain_lblMsg") != null && !string.IsNullOrEmpty(webBrowser.Document.GetElementById("ctl00_plhMain_lblMsg").InnerHtml))
//                            {
//                                string text = webBrowser.Document.GetElementById("ctl00_plhMain_lblMsg").InnerText;
//                                if (text.Contains(_currentTask.Receipt))
//                                {
//                                    Logger.Warning(string.Format("Заявка для {0} {1} уже зарегестрированна. Ответ сайта: {2}",_currentTask.LastName, _currentTask.Name, text));
//                                    if (string.IsNullOrEmpty(_currentTask.RegistrationInfo))
//                                        _currentTask.RegistrationInfo = "Please get info from site.";
//                                    deleteTask_click(new RegCount(), null);
//                                    break;
//                                }
//                                Logger.Error("Надо исправить ошибку: \r\n " + text);
//                                richText.Text = "Надо исправить ошибку: \r\n " + text;
//                                renewTask.Visible = true;
//                                deleteTask.Visible = true;
//                                _enum = RotEvents.FillReceipt;
//                                break;
//                            }
//                            Logger.Info(string.Format("{0}: Email: {1} Pass: {2}", _currentTask.City,_currentTask.Email, _currentTask.Password));
//                            webBrowser.Document.GetElementById("ctl00_plhMain_txtEmailID").SetAttribute("value", _currentTask.Email);
//                            webBrowser.Document.GetElementById("ctl00_plhMain_txtPassword").SetAttribute("value", _currentTask.Password);
//                            _enum = RotEvents.FirstCupture;
//                            webBrowser.Document.GetElementById("ctl00_plhMain_btnSubmitDetails").InvokeMember("click");
//                            break;
//                        }
//                    case RotEvents.FirstCupture:
//                        {
//                            ImageResolver.Instance.SystemDecaptcherLoad();
//                            if (webBrowser.Document.GetElementById("ctl00_plhMain_VS") != null && !string.IsNullOrEmpty(webBrowser.Document.GetElementById("ctl00_plhMain_VS").InnerText))
//                            {
//                                string error = webBrowser.Document.GetElementById("ctl00_plhMain_VS").InnerText;
//                                Logger.Error("Надо исправить ошибку: \r\n " + error);
//                                richText.Text ="Надо исправить ошибку: \r\n " + error;
//                                renewTask.Visible = true;
//                                deleteTask.Visible = true;
//                                _enum = RotEvents.FillEmail;
//                                break;
//                            }
//
//                            Logger.Warning("Дружищще, отправляй меня быстрее "+ _currentTask.GetInfo());
//                            richText.AppendText(_currentTask.GetInfo());
//                            webBrowser.Document.GetElementById("ctl00_plhMain_repAppVisaDetails_ctl01_tbxPPTEXPDT").SetAttribute("value", _currentTask.PassportEndDate);
//                            webBrowser.Document.GetElementById("ctl00_plhMain_repAppVisaDetails_ctl01_cboTitle").SetAttribute("selectedIndex", _currentTask.StatusCode);
//                            webBrowser.Document.GetElementById("ctl00_plhMain_repAppVisaDetails_ctl01_tbxFName").SetAttribute("value", _currentTask.Name);
//                            webBrowser.Document.GetElementById("ctl00_plhMain_repAppVisaDetails_ctl01_tbxLName").SetAttribute("value", _currentTask.LastName);
//                            webBrowser.Document.GetElementById("ctl00_plhMain_repAppVisaDetails_ctl01_tbxDOB").SetAttribute("value", _currentTask.Dob);
//                            webBrowser.Document.GetElementById("ctl00_plhMain_repAppVisaDetails_ctl01_tbxReturn").SetAttribute("value", _currentTask.ArrivalDt);
//
//                            for (int i = 0; i < this.webBrowser.Document.GetElementById("ctl00_plhMain_repAppVisaDetails_ctl01_cboNationality").Children.Count; i++)
//                            {
//                                HtmlElement child = this.webBrowser.Document.GetElementById("ctl00_plhMain_repAppVisaDetails_ctl01_cboNationality").Children[i];
//                                if (child.InnerText == _currentTask.Nationality)
//                                {
//                                    this.webBrowser.Document.GetElementById("ctl00_plhMain_repAppVisaDetails_ctl01_cboNationality").SetAttribute("selectedIndex", i.ToString());
//                                    break;
//                                }
//                            }
//
//                            decaptcherImage();
//                            //ctl00_plhMain_btnSubmit
//                            _enum = RotEvents.SecondCupture;
//                            if(ImageResolver.Instance.AutoResolveImage)
//                                webBrowser.Document.GetElementById("ctl00_plhMain_btnSubmit").InvokeMember("click");
//
//                            break;
//                        }
//                    case RotEvents.SecondCupture:
//                        {
//                            ImageResolver.Instance.SystemDecaptcherLoad();
//                            decaptcherImage();
//                            _enum = RotEvents.SelectTime;
//
//                            //date
//                            //class = OpenDateAllocated   <a>
//                            if (ImageResolver.Instance.AutoResolveImage)
//                                PressOnLinkOnCalendar();
//                            //===============================
//                            break;
//                        }
//                    case RotEvents.SelectTime:
//                        {
//                            if (webBrowser.Document.GetElementById("ctl00_plhMain_lblMsg") != null && !string.IsNullOrEmpty(webBrowser.Document.GetElementById("ctl00_plhMain_lblMsg").InnerHtml))
//                            {
//                                string text = webBrowser.Document.GetElementById("ctl00_plhMain_lblMsg").InnerText;
//                                Logger.Error("ошибкa: \r\n " + text);
//                                if (text.Contains("текст не відповідає символам на зображенні") && !ImageResolver.Instance.AutoResolveImage)
//                                {
//                                    if (_countAttempt-- != 0)
//                                    {
//                                        ImageResolver.Instance.SystemDecaptcherLoad();
//                                        decaptcherImage();
//                                        PressOnLinkOnCalendar();
//                                    }
//                                }
//                                break;
//                            }
//
//                            //text
//                            //name =ctl00$plhMain$MyCaptchaControl1
//                            decaptcherImage();
//                            _enum = RotEvents.Stop;
//                            //_blockAlert = true;
//                            //table id = ctl00_plhMain_gvSlot
//                            // a class 
//                            if (ImageResolver.Instance.AutoResolveImage)
//                                PressOnLinkByClass("backlink");
//
//                            break;
//                        }
//                    case RotEvents.Stop:
//                        {
//                            if (webBrowser.Document.GetElementById("ctl00_plhMain_lblMsg") != null && !string.IsNullOrEmpty(webBrowser.Document.GetElementById("ctl00_plhMain_lblMsg").InnerHtml))
//                            {
//                                string text = webBrowser.Document.GetElementById("ctl00_plhMain_lblMsg").InnerText;
//                                Logger.Error("ошибкa: \r\n " + text);
//                                if (text.Contains("текст не відповідає символам на зображенні") && !ImageResolver.Instance.AutoResolveImage)
//                                {
//                                    if (_countAttempt-- != 0)
//                                    {
//                                        ImageResolver.Instance.SystemDecaptcherLoad();
//                                        decaptcherImage();
//                                        PressOnLinkByClass("backlink");
//                                    }
//                                }
//                                break;
//                            }
//                            //_blockAlert = false;
//                            if (!ImageResolver.Instance.AutoResolveImage)
//                            {
//                                if (webBrowser.Document.GetElementById("ApplicantDetalils") != null && !string.IsNullOrEmpty(webBrowser.Document.GetElementById("ApplicantDetalils").InnerText))
//                                {
//                                    Logger.Info("Заявка успешно зарегистрирована. ");
//                                    string registrationInfo = webBrowser.Document.GetElementById("ApplicantDetalils").InnerText;
//                                    Logger.Info(registrationInfo);
//                                    _currentTask.RegistrationInfo = registrationInfo;
//                                }
//                                renewTask.Visible = true;
//                                deleteTask.Visible = true;
//                            }
//                            else
//                            {
//                                if (webBrowser.Document.GetElementById("ApplicantDetalils") != null && !string.IsNullOrEmpty(webBrowser.Document.GetElementById("ApplicantDetalils").InnerText))
//                                {
//                                    Logger.Info("Заявка успешно зарегистрирована. ");
//                                    string registrationInfo = webBrowser.Document.GetElementById("ApplicantDetalils").InnerText;
//                                    Logger.Info(registrationInfo);
//                                    _currentTask.RegistrationInfo = registrationInfo;
//                                    deleteTask_click(null, null);
//                                }
//                                else
//                                {
//                                    Logger.Info("Регистрация заявки не состоялась на этапе выбора времени.");
//                                    renewTask_click(null, null);
//                                }
//                            }
//                            
//                            TurnAlarmOn(false);
//                            
//                            break;
//                        }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                //_blockAlert = false;
                TurnAlarmOn(false);
                if (VisaEvent != null)
                    VisaEvent(this, false);
                _allowStep = true;
                _enum = RotEvents.Start;
            }
        }

        private void SelectFromCombo(string id, string value)
        {
            for (int i = 0; i < this.webBrowser.Document.GetElementById(id).Children.Count; i++)
            {
                HtmlElement child = this.webBrowser.Document.GetElementById(id).Children[i];
                if (child.InnerText == value)
                {
                    this.webBrowser.Document.GetElementById(id).SetAttribute("selectedIndex", i.ToString());
                    break;
                }
            }
        }

        private void PressOnLinkOnCalendar()
        {
            var links = webBrowser.Document.GetElementsByTagName("a");
            foreach (HtmlElement link in links)
            {
                //if (link.GetAttribute("classname") == "OpenDateAllocated")
                //{
                //    link.InvokeMember("click");
                //    break; 
                //}
                if (!link.GetAttribute("Title").Contains("Go to the"))
                {
                    link.InvokeMember("click");
                    break;
                }
            }
        }

        private void PressOnLinkByClass(string className)
        {
            var links = webBrowser.Document.GetElementsByTagName("a");
            foreach (HtmlElement link in links)
            {
                if (link.GetAttribute("classname") == className)
                {
                    link.InvokeMember("click");
                    break;
                }
            }
        }

        private void decaptcherImage()
        {
            if(!ImageResolver.Instance.AutoResolveImage)
                return;
            Logger.Info("Start parsing cupture");
            string value = ImageResolver.Instance.RecognizePictureGetString(ImageToByte(getFirstImage()));
            Logger.Info("End parsing cupture");

            //ctl00$plhMain$MyCaptchaControl1
            HtmlElementCollection elems = webBrowser.Document.All.GetElementsByName("ctl00$plhMain$MyCaptchaControl1");
            HtmlElement elem = null;
            if (elems != null && elems.Count > 0)
            {
                elem = elems[0];
                elem.SetAttribute("value", value);
            }
        }

        [ComImport, InterfaceType((short)1), Guid("3050F669-98B5-11CF-BB82-00AA00BDCE0B")]
        private interface IHTMLElementRenderFixed
        {
            void DrawToDC(IntPtr hdc);
            void SetDocumentPrinter(string bstrPrinterName, IntPtr hdc);
        }

        public Bitmap getFirstImage()
        {
            Logger.Info("Start get Image");

            IHTMLDocument2 doc = (IHTMLDocument2)webBrowser.Document.DomDocument;
            foreach (IHTMLImgElement img in doc.images)
            {
                IHTMLElementRenderFixed render = (IHTMLElementRenderFixed)img;
                Bitmap bmp = new Bitmap(img.width, img.height);
                Graphics g = Graphics.FromImage(bmp);
                IntPtr hdc = g.GetHdc();
                render.DrawToDC(hdc);
                g.ReleaseHdc(hdc);
                Logger.Info("Stop get Image");
                return bmp;
                //bmp.Save(@"C:\nameProp.bmp");


                //imgRange.add((IHTMLControlElement)img);

                //imgRange.execCommand("Copy", false, null);

                //using (Bitmap bmp = (Bitmap)Clipboard.GetDataObject().GetData(DataFormats.Bitmap))
                //{
                //    bmp.Save(@"C:\" + img.nameProp);
                //}
            }
            return null;
        }

        public Bitmap GetImage(string id)
        {
            HtmlElement e = webBrowser.Document.GetElementById(id);
            IHTMLImgElement img = (IHTMLImgElement)e.DomElement;
            IHTMLElementRenderFixed render = (IHTMLElementRenderFixed)img;

            Bitmap bmp = new Bitmap(e.OffsetRectangle.Width, e.OffsetRectangle.Height); 
            Graphics g = Graphics.FromImage(bmp);
            IntPtr hdc = g.GetHdc();
            render.DrawToDC(hdc);
            g.ReleaseHdc(hdc);
            return bmp;
        }

        private byte[] ImageToByte(Image img)
        {
            Logger.Info("Start ImageToByte");

            byte[] byteArray = new byte[0];

            ImageCodecInfo jgpEncoder = GetEncoder(ImageFormat.Jpeg);
            // Create an Encoder object based on the GUID
            // for the Quality parameter category.
            Encoder myEncoder = Encoder.Quality;
            // Create an EncoderParameters object.
            // An EncoderParameters object has an array of EncoderParameter
            // objects. In this case, there is only one
            // EncoderParameter object in the array.
            EncoderParameters myEncoderParameters = new EncoderParameters(1);
            EncoderParameter myEncoderParameter = new EncoderParameter(myEncoder,50L);
            myEncoderParameters.Param[0] = myEncoderParameter;

            using (MemoryStream stream = new MemoryStream())
            {
                img.Save(stream, jgpEncoder, myEncoderParameters);//ImageFormat.Jpeg);
                stream.Close();

                byteArray = stream.ToArray();
            }
            Logger.Info("Stop ImageToByte");
            return byteArray;
        }

        private ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }

        public void CheckOnDeleteTask(KonsulatTask visaTask)
        {
            Tasks.Remove(visaTask);
            if (Tasks.Count == 0 && TabEvent != null)
            {
                TabEvent(_tabPage);
            }
            _enum = RotEvents.Start;
            _allowStep = true;
            TurnAlarmOn(false);
        }

        private void TurnAlarmOn(bool on)
        {
            _playSound = on;
            if (TabEventEx != null)
                TabEventEx(_tabPage, on);
        }

        private void playSound()
        {
            sp.Play();
        }

        private DateTime ProcessDate(string xnya)
        {
            DateTime dt = DateTime.MinValue;
            try
            {
                string[] splitted = xnya.Replace("Найближча доступна дата для реєстрації є ", "").Split('.');
                string s = string.Format("{0}/{1}/{2}", splitted[0], Const.GetMonthAsInt(splitted[1]), splitted[2]);
                dt = DateTime.ParseExact(s, Const.DateFormat, CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
            return dt;
        }

        private void pressOnLink(WebBrowser webBrowser, string text)
        {
            try
            {
                var link = webBrowser.Document.GetElementsByTagName("a")[0];
                if (link.InnerText == text)
                    link.InvokeMember("click");
            }
            catch
            {
                foreach (HtmlElement link in webBrowser.Document.GetElementsByTagName("a"))
                {
                    if (link.InnerText == text)
                        link.InvokeMember("click");
                }
            }
        }

        #region Proxy

        
        private void SetProxyServer(string proxy)
        {
            //Create structure
            INTERNET_PROXY_INFO proxyInfo = new INTERNET_PROXY_INFO();

            if (proxy == null)
            {
                proxyInfo.dwAccessType = INTERNET_OPEN_TYPE_DIRECT;
            }
            else
            {
                proxyInfo.dwAccessType = INTERNET_OPEN_TYPE_PROXY;
                proxyInfo.proxy = Marshal.StringToHGlobalAnsi(proxy);
                proxyInfo.proxyBypass = Marshal.StringToHGlobalAnsi("local");
            }

            // Allocate memory
            IntPtr proxyInfoPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf(proxyInfo));

            // Convert structure to IntPtr
            Marshal.StructureToPtr(proxyInfo, proxyInfoPtr, true);
            bool returnValue = InternetSetOption(IntPtr.Zero, INTERNET_OPTION_PROXY,
                proxyInfoPtr, Marshal.SizeOf(proxyInfo));
        }

        #region IOleClientSite Members

        public void SaveObject()
        {
            // TODO:  Add Form1.SaveObject implementation
        }

        public void GetMoniker(uint dwAssign, uint dwWhichMoniker, object ppmk)
        {
            // TODO:  Add Form1.GetMoniker implementation
        }

        public void GetContainer(object ppContainer)
        {
            ppContainer = this;
        }

        public void ShowObject()
        {
            // TODO:  Add Form1.ShowObject implementation
        }

        public void OnShowWindow(bool fShow)
        {
            // TODO:  Add Form1.OnShowWindow implementation
        }

        public void RequestNewObjectLayout()
        {
            // TODO:  Add Form1.RequestNewObjectLayout implementation
        }

        #endregion

        #region IServiceProvider Members

        public int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject)
        {
            int nRet = guidService.CompareTo(IID_IAuthenticate);
            if (nRet == 0)
            {
                nRet = riid.CompareTo(IID_IAuthenticate);
                if (nRet == 0)
                {
                    ppvObject = Marshal.GetComInterfaceForObject(this, typeof(IAuthenticate));
                    return S_OK;
                }
            }

            ppvObject = new IntPtr();
            return INET_E_DEFAULT_ACTION;
        }

        #endregion

        #region IAuthenticate Members

        public int Authenticate(ref IntPtr phwnd, ref IntPtr pszUsername, ref IntPtr pszPassword)
        {
            IntPtr sUser = Marshal.StringToCoTaskMemAuto(_currentUsername);
            IntPtr sPassword = Marshal.StringToCoTaskMemAuto(_currentPassword);

            pszUsername = sUser;
            pszPassword = sPassword;
            return S_OK;
        }

        #endregion
    }

    public struct INTERNET_PROXY_INFO
    {
        public int dwAccessType;
        public IntPtr proxy;
        public IntPtr proxyBypass;
    }

    #region COM Interfaces

    [ComImport, Guid("00000112-0000-0000-C000-000000000046"),
    InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOleObject
    {
        void SetClientSite(IOleClientSite pClientSite);
        void GetClientSite(IOleClientSite ppClientSite);
        void SetHostNames(object szContainerApp, object szContainerObj);
        void Close(uint dwSaveOption);
        void SetMoniker(uint dwWhichMoniker, object pmk);
        void GetMoniker(uint dwAssign, uint dwWhichMoniker, object ppmk);
        void InitFromData(IDataObject pDataObject, bool
            fCreation, uint dwReserved);
        void GetClipboardData(uint dwReserved, IDataObject ppDataObject);
        void DoVerb(uint iVerb, uint lpmsg, object pActiveSite,
            uint lindex, uint hwndParent, uint lprcPosRect);
        void EnumVerbs(object ppEnumOleVerb);
        void Update();
        void IsUpToDate();
        void GetUserClassID(uint pClsid);
        void GetUserType(uint dwFormOfType, uint pszUserType);
        void SetExtent(uint dwDrawAspect, uint psizel);
        void GetExtent(uint dwDrawAspect, uint psizel);
        void Advise(object pAdvSink, uint pdwConnection);
        void Unadvise(uint dwConnection);
        void EnumAdvise(object ppenumAdvise);
        void GetMiscStatus(uint dwAspect, uint pdwStatus);
        void SetColorScheme(object pLogpal);
    }

    [ComImport, Guid("00000118-0000-0000-C000-000000000046"),
    InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOleClientSite
    {
        void SaveObject();
        void GetMoniker(uint dwAssign, uint dwWhichMoniker, object ppmk);
        void GetContainer(object ppContainer);
        void ShowObject();
        void OnShowWindow(bool fShow);
        void RequestNewObjectLayout();
    }

    [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"),
    InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
    ComVisible(false)]
    public interface IServiceProvider
    {
        [return: MarshalAs(UnmanagedType.I4)]
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
    }

    [ComImport, Guid("79EAC9D0-BAF9-11CE-8C82-00AA004BA90B"),
    InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
    ComVisible(false)]
    public interface IAuthenticate
    {
        [return: MarshalAs(UnmanagedType.I4)]
        [PreserveSig]
        int Authenticate(ref IntPtr phwnd, ref IntPtr pszUsername, ref IntPtr pszPassword);
    }

    #endregion

        #endregion
}
