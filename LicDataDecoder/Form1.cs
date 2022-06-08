using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LicDataDecoder
{
    public partial class Form1 : Form
    {
        //Версии необходимых компонентов в системе
        string JRE = "";
        string RING = "";       

        string path; //путь к файлу
        string fileName; //имя файла
        string folderName; //путь к папке с файлом

        public Form1()
        {
            InitializeComponent();

            this.Width = 500;

            checkAbilityAcync();
            //button1.Enabled = true;
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            if (openFileDialog1.ShowDialog() == DialogResult.Cancel) return;
            textBox1.Text = openFileDialog1.FileName;
            textBox2.Text = "Подождите...";
            textBox3.Text = "Параметры компьютера, получившего лицензию. Не все из них являются ключевыми.";
            textBox4.Text = "Здесь будет выведена информация о параметрах текущего компьютера";

            try
            {
                setFileNameAndPath();

                string[] results = await Task.Factory.StartNew<string[]>(
                                             () => decodeLicenceFile(),
                                             TaskCreationOptions.LongRunning);

                textBox2.Text = results[0];
                if (ExternalMode.Checked)
                {
                    textBox3.Text = results[1];
                    textBox4.Text = results[2];
                }

            }
            catch
            {
                textBox2.Text = "Выбранный файл не является лицензией или поврежден.";
                File.Delete(folderName + "\\" + fileName);
            }
        }

        private void setFileNameAndPath()
        {
            path = textBox1.Text;
            fileName = path.Substring(path.LastIndexOf(@"\") + 1, path.Length - path.LastIndexOf('\\') - 1);
            folderName = path.Substring(0, path.Length - fileName.Length - 1);
          
            string tempFolder = System.IO.Path.GetTempPath() + "LicDataDecoder";            
            Directory.CreateDirectory(tempFolder);
            folderName = tempFolder;
            File.Copy(path, tempFolder+"\\"+fileName, true);
          
            //У текущей версии Ring есть баг - если в папке с указанной лицензией находится сломанная лицензия, то она зачем-то выводит пытается просканировать и её.
            //Во избежание этого реализовал копирование текущего файла лицензии во временный каталог с последующим удалением.
        }

        private string[] decodeLicenceFile()
        {

            string[] results = new string[3] {"", "", ""};

            string result = "";
            string licName = "";
            string pinCode = "";

            try
            {               
                licName = getLicName();
                pinCode = licName.Substring(0, 15);
            }
            catch
            {
                results[0] = "Ошибка при определении внутреннего имени лицензии. Возможные причины:" + Environment.NewLine+ Environment.NewLine + "Файл лицензии поврежден" + Environment.NewLine + "Файл не является лицензией 1С" + Environment.NewLine + "В системе присутствуют остатки от предыдущих версий Ring и License" + Environment.NewLine + "Обновился формат лицензий и текущая версия LicenseTools его не поддерживает.";
                File.Delete(folderName + "\\" + fileName);
                return results;
            }

            try
            {
                result += getLicData(licName) + System.Environment.NewLine;
                result += "--------------------------------------------------------------------------------------";
                result += "Текущий пин-код: " + buildPinCode(pinCode);
                result += "--------------------------------------------------------------------------------------";
                result += System.Environment.NewLine + System.Environment.NewLine;
            }
            catch
            {
                results[0] = "Ошибка при извлечении ликдаты из лицензии.";
                File.Delete(folderName + "\\" + fileName);
                return results;
            }

            try
            {
                result += getValidateData(licName);
                results[0] = result;
            }
            catch
            {
                results[0] = "Ошибка при сопоставлении ключевых параметров этого компьютера с параметрами из лицензии";
                File.Delete(folderName + "\\" + fileName);
                return results;
            }


            //расширенный режим
            try
            {
                if (ExternalMode.Checked)
                {
                    string debugInfo = getDebugInfo(licName);

                    string[] debugMessages = debugInfo.Split(new string[] { "[DEBUG ] com._1c.license.activator.crypt.Converter - getLicensePermitFromBase64 : Request : Computer info : \r\n" }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (string message in debugMessages)
                    {
                        if (message.Contains("pin : " + pinCode))
                        {
                            int end = message.IndexOf("Customer info :");
                            string licHWConfig = message.Substring(0, end);
                            results[1] = "Параметры компьютера, получившего лицензию. Не все из них являются ключевыми." + System.Environment.NewLine + System.Environment.NewLine + licHWConfig;
                        }
                    }

                    debugMessages = debugInfo.Split(new string[] { "[DEBUG ] com._1c.license.activator.hard.HardInfo - computer info : " }, StringSplitOptions.RemoveEmptyEntries);
                    string HWConfig = debugMessages[1].Substring(0, debugMessages[1].IndexOf("\r\n\r\n"));
                    results[2] = "Параметры этого компьютера, которые могли бы записаться в лицензию" + System.Environment.NewLine + System.Environment.NewLine + HWConfig;

                }
            }
            catch
            {
                results[0] = "Ошибка при работе в расширенном режиме";
                File.Delete(folderName + "\\" + fileName);
                return results;
            }                       

            File.Delete(folderName + "\\" + fileName);

            return results;


        } //фоновый метод декодирования файла лицензии

        private string getDebugInfo(string licName)
        {
            string debugInfo = "";

            Process process = new Process();
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = "/C ring -l \"debug\" license validate --name " + licName + " --path \"" + folderName + "\"" + " --send-statistics \"false\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            StreamReader reader = process.StandardOutput;
            debugInfo = reader.ReadToEnd();

            return debugInfo;
        } //получить полную конфигурацию компьютера

        private string getLicName()
        {
            string licName = "";

            Process process = new Process();
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = "/C ring license list --path \"" + folderName + "\"" + " --send-statistics \"false\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            StreamReader reader = process.StandardOutput;
            string output = reader.ReadToEnd();

            //Т.к. команда list выдает список всех лицензий в папке с указанным файлом, то, 
            //чтобы получить внутреннее название именно указанного файла, нужно разбить получившийся
            //список на строки и найти в нем строку, содержащую название указанного файла, а потом обрезать её начало.

            string[] stringsArray = output.Split(Environment.NewLine.ToCharArray());
            foreach (string str in stringsArray)
            {
                if (str.EndsWith(fileName + "\")"))
                {

                    int indexOfChar = str.IndexOf('('); // равно 4
                    licName = str.Substring(0, indexOfChar);
                    return licName;
                }
            }

            return licName;
        } //получить внутреннее название лицензии

        private string getLicData(string licName)
        {
            string licData = "";

            Process process = new Process();
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = "/C ring license info --name " + licName + " --path \"" + folderName + "\"" + " --send-statistics \"false\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            StreamReader reader = process.StandardOutput;
            licData = reader.ReadToEnd();

            return licData;
        } //получить ликдату

        private string getValidateData(string licName)
        {
            string validateData = "";

            Process process = new Process();
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = "/C ring license validate --name " + licName + " --path \"" + folderName + "\"" + " --send-statistics \"false\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            StreamReader reader = process.StandardOutput;
            validateData = reader.ReadToEnd();


            string str = "Проверка лицензии завершилась с ошибкой.\r\nПо причине: ";
            if (validateData.Contains(str))
            {
                validateData = validateData.Remove(validateData.IndexOf(str), str.Length);
                validateData = "Ключевые параметры компьютера не соответствуют лицензии." + System.Environment.NewLine
                  + "Для получения полного списка оборудования включите подробный режим." + System.Environment.NewLine
                  + System.Environment.NewLine + validateData;
            }


            return validateData;
        } //получить информацию о железе компьютера

        private StringBuilder buildPinCode(string pinCode)
        {
            StringBuilder extPinCode = new StringBuilder(pinCode.Length + 4);

            List<int> dashPositionsList = new List<int>() { 4, 7, 10, 13 };
            for (int i = 0; i < pinCode.Length; i++)
            {
                if (dashPositionsList.Contains(i + 1))
                {
                    extPinCode.Append("-");
                    extPinCode.Append(pinCode[i]);
                }
                else
                {
                    extPinCode.Append(pinCode[i]);
                }
            }

            return extPinCode;
        } //построитель пин-кода с тире

        private void оПрограммеToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Form2 aboutForm = new Form2();
            aboutForm.ShowDialog();
        }

        private async void checkAbilityAcync()
        {

            bool READY = true;

            textBox2.Text += Environment.NewLine + Environment.NewLine + "Выполняется проверка возможности декодирования лицензий..." + Environment.NewLine + Environment.NewLine;

            JRE = await Task.Factory.StartNew<string>(
                                       () => checkJRE(),
                                       TaskCreationOptions.LongRunning);

            RING = await Task.Factory.StartNew<string>(
                                     () => checkRING(),
                                     TaskCreationOptions.LongRunning);

            switch (JRE)
                {
                    case "0":
                        textBox2.Text += "В системе отсутствует JRE. Для работы необходима версия JRE или JDK 1.8.161 или новее ";
                        READY &= false;
                        break;
                    case "error":
                        textBox2.Text += "Неозможно определить версию JRE!";
                        READY &= false;
                        break;
                    default:                     
                        textBox2.Text += "Версия JRE: " + JRE;

                        // у восьмой джавы версии имеют вид 1.8.0_256
                        // более новые джавы версии имеют вид 11.0.15

                        if (JRE.Substring(0, 3)=="1.8") //проверим минимальную версию восьмой джавы, более новую джаву нет смысла проверять
                           {
                                if (Int32.Parse(JRE.Substring(6, 3)) < 161)
                                {
                                    textBox2.Text += Environment.NewLine + "Для работы необходима версия JRE или JDK 1.8.161 или новее";
                                    READY &= false;
                                }
                           }

                        break;
                }

            textBox2.Text += Environment.NewLine + Environment.NewLine;

            switch (RING)
            {
                case "0":
                    textBox2.Text += "В системе отсутствует RING. Для работы необходима версия RING не ниже 0.11.5-3. Утилита поставляется в комплекте с дистрибутивом технологической платформы 8.3.14.1565 в папке \"license-tools\". Запустите файл 1ce-installer.cmd с правами администратора для установки.";
                    READY &= false;
                    break;
                case "error":
                    textBox2.Text += "Неозможно определить версию RING!";
                    READY &= false;
                    break;
                default:
                    textBox2.Text += "Версия RING: " + RING;
                    if (Int32.Parse(RING.Substring(5, 1)) < 5 & Int32.Parse(RING.Substring(2, 2)) < 12)
                    {
                        textBox2.Text += Environment.NewLine + "Для работы необходима версия RING не ниже 0.11.5-3. Утилита поставляется в комплекте с дистрибутивом технологической платформы 8.3.14.1565 в папке \"license-tools\". Удалите старые версии Ring и License и запустите файл 1ce-installer.cmd с правами администратора для установки.";
                        READY &= false;
                    }
                    break;
            }
            
            if (READY)
            {
                textBox2.Text += Environment.NewLine + Environment.NewLine + "Программа готова к работе!";
                button1.Enabled = true;
            }

        }

        private string checkJRE()
        {

            try
            {
                Process process = new Process();
                process.StartInfo.FileName = "cmd.exe";
                process.StartInfo.Arguments = "/C java -version";
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                
                StreamReader reader = process.StandardError;
                string output = reader.ReadToEnd();

                //возможны два варианта
                //1. java version "1.8.0_256", 
                //2. "java" не является внутренней или внешней бла бла бла

                if (!output.Contains("\"java\"")) //джава нашлась, определяем версию
                {
                    int firstQuotesPosition = output.IndexOf("\"");                         //первые кавычки
                    int secondQuotesPosition = output.IndexOf("\"", firstQuotesPosition+1); //выторые кавычки
                    int length = secondQuotesPosition - firstQuotesPosition - 1;            //количество символов между кавычками
                    return output.Substring(firstQuotesPosition + 1, length);               //текст между кавычками
                }
                else return "0"; //джава отсутствует

                //Передаю огненный привет разработчикам java, которые посчитали хорошей идеей выводить информацию
                //в стандартный поток ошибок вместо стандартного потока вывода

            }
            catch (Exception e)
            {
                return "error";
            }

        }

        private string checkRING()
        {

            try
            {
                if (checkJRE()=="0") return "error";
                Process process = new Process();
                process.StartInfo.FileName = "cmd.exe";
                process.StartInfo.Arguments = "/C ring --version";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.Start();

                StreamReader reader = process.StandardOutput;
                string output = reader.ReadToEnd();
                if (!output.Contains("\"ring\"") && output!="")
                {
                    return output.Replace("\r\n", "");
                }
                else return "0";

            }
            catch (Exception e)
            {
                return "error";
            }

        }       

        private void externalModeCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            Rectangle r = Screen.FromControl(this).WorkingArea;

            if (this.Width == 500)
            {
                for (int i = 500; i <= 1410; i += 10)
                {
                    this.Width = i;
                    this.Bounds = new Rectangle((r.Width - this.Width) / 2, (r.Height - this.Height) / 2, this.Width, this.Height);                                      
                }
                System.Threading.Thread.Sleep(1);
            }
            else
            {
                for (int i = 1410; i >= 500; i -= 10)
                {
                    this.Width = i;
                    this.Bounds = new Rectangle((r.Width - this.Width) / 2, (r.Height - this.Height) / 2, this.Width, this.Height);
                    System.Threading.Thread.Sleep(1);
                    
                }
            }


            //Без анимации

            //if (this.Width == 500)
            //{
            //    this.Width = 1410;
            //}
            //else
            //{
            //    this.Width = 500;
            //}
            //Rectangle r = Screen.FromControl(this).WorkingArea;
            //this.Bounds = new Rectangle((r.Width-this.Width)/2, (r.Height-this.Height)/2, this.Width, this.Height);

        }

        private void useStandartFolderCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (useStandartFolderCheckBox.Checked == true)
            {
                openFileDialog1.InitialDirectory = @"C:\ProgramData\1C\licenses";
            }
            else
            {
                openFileDialog1.InitialDirectory = "";
            }
        }
    }
}