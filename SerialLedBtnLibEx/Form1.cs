using System.IO.Ports;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Button;

namespace SerialLedBtnLibEx
{
    public partial class Form1 : Form
    {
        JsonSerialController jsonSerialController;

        public Form1()
        {
            InitializeComponent();
        }



        private async void Form1_Load(object sender, EventArgs e)
        {
            //Set up GUI
            checkBox1.Text = "LED1";
            checkBox2.Text = "SW1";
            checkBox3.Text = "LED2";
            checkBox4.Text = "SW2";
            checkBox5.Text = "LED3";
            checkBox6.Text = "SW3";
            checkBox7.Text = "LED4";

            // Connect over BLE (Nordic UART Service) instead of the wired COM port.
            // The device name must match the peripheral's advertised name.
            BleNusEx _serialDev = new BleNusEx("Pico-NUS");
            await _serialDev.ConnectAsync(TimeSpan.FromSeconds(15));

            // Connect using USB CDC-ACM (Communications Device Class - Abstract Control Model) ( USB <-> RS232 )
            // PortName = Windows Serial Por nr , BaudRate = Transfer Speed
            
            //SerialPortEx _serialDev = new SerialPortEx { PortName = "COM3", BaudRate = 115200 };
            //_serialDev.Open();

            // _serialDev implements ISerialDataReadWrite, so it drops straight into the JsonSerialController.
            jsonSerialController = new JsonSerialController(_serialDev);
            jsonSerialController.ButtonControl["SW1"].SwitchStateChanged += Form1_SwitchStateChanged;
            jsonSerialController.ButtonControl["SW2"].SwitchStateChanged += Form1_SwitchStateChanged;
            jsonSerialController.ButtonControl["SW3"].SwitchStateChanged += Form1_SwitchStateChanged;
        }
        // Husk:
        // Nå er vi i serie-port tråden (bakgrunnstråden til serieportens datamotaks tråd)
        // For å snakke med GUI må vi bruke BeginInvoke for å kjøre koden i GUI tråden.
        private void Form1_SwitchStateChanged(object? sender, SwitchEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"Form1::Form1_SwitchStateChanged ID: {e.Id} Value: {e.Value}");

            if (e.Id == "SW1")
            { checkBox2.BeginInvoke(() => checkBox2.Checked = Convert.ToBoolean(e.Value)); }

            if (e.Id == "SW2")
            { checkBox4.BeginInvoke(() => checkBox4.Checked = Convert.ToBoolean(e.Value)); }

            if (e.Id == "SW3")
            { checkBox6.BeginInvoke(() => checkBox6.Checked = Convert.ToBoolean(e.Value)); }

        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            // checkBox3.Checked returnerer en bool, vi må derfor konvertere den til en int (0 eller 1) 
            jsonSerialController.LedControl["LED1"].Value = Convert.ToInt32(checkBox1.Checked);
        }

        private void checkBox3_CheckedChanged(object sender, EventArgs e)
        {           
            jsonSerialController.LedControl["LED2"].Value = Convert.ToInt32(checkBox3.Checked);

        }

        private void checkBox5_CheckedChanged(object sender, EventArgs e)
        {
            jsonSerialController.LedControl["LED3"].Value = Convert.ToInt32(checkBox5.Checked);

        }

        private void checkBox7_CheckedChanged(object sender, EventArgs e)
        {
            jsonSerialController.LedControl["LED4"].Value = Convert.ToInt32(checkBox7.Checked);

        }
    }
}
