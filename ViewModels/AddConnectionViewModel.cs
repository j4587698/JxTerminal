using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JxTerminal.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO.Ports;

namespace JxTerminal.ViewModels;

public partial class AddConnectionViewModel : ViewModelBase
{
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(IsSsh))]
    [NotifyPropertyChangedFor(nameof(IsTelnet))]
    [NotifyPropertyChangedFor(nameof(IsSerial))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private ConnectionType _type = ConnectionType.Ssh;

    partial void OnTypeChanged(ConnectionType oldValue, ConnectionType newValue)
    {
        // Move to the new protocol's well-known port, unless the user typed a custom one.
        if (Port == DefaultPortFor(oldValue))
        {
            Port = DefaultPortFor(newValue);
        }
    }

    private static int DefaultPortFor(ConnectionType type) => type switch
    {
        ConnectionType.Telnet => 23,
        ConnectionType.Ssh => 22,
        _ => 0
    };

    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(IsPasswordAuth))]
    [NotifyPropertyChangedFor(nameof(IsKeyAuth))]
    [NotifyPropertyChangedFor(nameof(IsAskAuth))]
    private SshAuthenticationType _authenticationType = SshAuthenticationType.Password;

    [ObservableProperty] private string _name = JxTerminal.Services.LocalizationService.Instance["NewConnection"];
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SaveCommand))] private string _host = "";
    [ObservableProperty] private int _port = 22;
    [ObservableProperty] private string _username = "root";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _keyFilePath = "";
    [ObservableProperty] private string _passphrase = "";
    [ObservableProperty] private string _selectedGroup = "Default";

    // Serial
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SaveCommand))] private string _serialPortName = "";
    [ObservableProperty] private int _baudRate = 9600;
    [ObservableProperty] private int _dataBits = 8;
    [ObservableProperty] private Parity _parity = Parity.None;
    [ObservableProperty] private StopBits _stopBits = StopBits.One;
    [ObservableProperty] private Handshake _handshake = Handshake.None;

    public List<int> AvailableBaudRates { get; } = new List<int> { 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200 };
    public List<int> AvailableDataBits { get; } = new List<int> { 5, 6, 7, 8 };
    public List<Parity> AvailableParities { get; } = new List<Parity> { Parity.None, Parity.Even, Parity.Odd, Parity.Mark, Parity.Space };
    public List<StopBits> AvailableStopBits { get; } = new List<StopBits> { StopBits.One, StopBits.OnePointFive, StopBits.Two };
    public List<Handshake> AvailableHandshakes { get; } = new List<Handshake> { Handshake.None, Handshake.XOnXOff, Handshake.RequestToSend, Handshake.RequestToSendXOnXOff };
    
    [ObservableProperty] private List<string> _availableSerialPorts = new();

    public bool IsSsh
    {
        get => Type == ConnectionType.Ssh;
        set
        {
            if (value) Type = ConnectionType.Ssh;
            OnPropertyChanged(nameof(IsSsh));
            OnPropertyChanged(nameof(IsTelnet));
            OnPropertyChanged(nameof(IsSerial));
        }
    }

    public bool IsTelnet
    {
        get => Type == ConnectionType.Telnet;
        set
        {
            if (value) Type = ConnectionType.Telnet;
            OnPropertyChanged(nameof(IsSsh));
            OnPropertyChanged(nameof(IsTelnet));
            OnPropertyChanged(nameof(IsSerial));
        }
    }

    public bool IsSerial
    {
        get => Type == ConnectionType.Serial;
        set
        {
            if (value) Type = ConnectionType.Serial;
            OnPropertyChanged(nameof(IsSsh));
            OnPropertyChanged(nameof(IsTelnet));
            OnPropertyChanged(nameof(IsSerial));
            if (value) RefreshSerialPorts();
        }
    }
    
    public bool IsPasswordAuth => AuthenticationType == SshAuthenticationType.Password;
    public bool IsKeyAuth => AuthenticationType == SshAuthenticationType.Key;
    public bool IsAskAuth => AuthenticationType == SshAuthenticationType.Ask;


    public List<string> AvailableGroups { get; }

    public void Close() => RequestClose?.Invoke(this, null);
    public event EventHandler<object?>? RequestClose;

    public AddConnectionViewModel(IEnumerable<string> groups)
    {
        AvailableGroups = groups.ToList();
        if (AvailableGroups.Count > 0)
            SelectedGroup = AvailableGroups[0];
            
        RefreshSerialPorts();
    }

    public AddConnectionViewModel() : this(new List<string> { "Default" }) { }

    public AddConnectionViewModel(SavedConnection existingConnection, IEnumerable<string> groups)
        : this(groups)
    {
        _name = existingConnection.Name;
        _type = existingConnection.Type;
        _host = existingConnection.Host;
        _port = existingConnection.Port;
        _username = existingConnection.Username;
        _password = existingConnection.Password;
        _authenticationType = existingConnection.AuthenticationType;
        _keyFilePath = existingConnection.KeyFilePath;
        _passphrase = existingConnection.Passphrase;
        
        // Serial
        _serialPortName = existingConnection.SerialPortName;
        _baudRate = existingConnection.BaudRate;
        _dataBits = existingConnection.DataBits;
        _parity = existingConnection.Parity;
        _stopBits = existingConnection.StopBits;
        _handshake = existingConnection.Handshake;

        if (groups.Contains(existingConnection.GroupName))
            SelectedGroup = existingConnection.GroupName;
        else if (AvailableGroups.Count > 0)
            SelectedGroup = AvailableGroups[0];

        _id = existingConnection.Id;
    }

    private Guid _id = Guid.NewGuid();

    public Func<Task<string?>>? RequestPickKeyFile;

    [RelayCommand]
    public async Task SelectKeyFile()
    {
        if (RequestPickKeyFile != null)
        {
            var file = await RequestPickKeyFile.Invoke();
            if (!string.IsNullOrEmpty(file))
            {
                KeyFilePath = file;
            }
        }
    }

    [ObservableProperty] private string _serialPortPlaceholder = "";

    [RelayCommand]
    public void RefreshSerialPorts()
    {
        try
        {
            AvailableSerialPorts = SerialPort.GetPortNames().ToList();
            if (AvailableSerialPorts.Count > 0)
            {
                 if (string.IsNullOrEmpty(SerialPortName)) SerialPortName = AvailableSerialPorts[0];
                 SerialPortPlaceholder = JxTerminal.Services.LocalizationService.Instance["SelectPort"];
            }
            else
            {
                 SerialPortPlaceholder = JxTerminal.Services.LocalizationService.Instance["NoDevicesFound"];
            }
        }
        catch (Exception)
        {
            AvailableSerialPorts = new List<string>();
            SerialPortPlaceholder = JxTerminal.Services.LocalizationService.Instance["NoDevicesFound"];
        }
    }

    /// <summary>
    /// A connection is only savable once its target is filled in.
    /// </summary>
    private bool CanSave() => Type == ConnectionType.Serial
        ? !string.IsNullOrWhiteSpace(SerialPortName)
        : !string.IsNullOrWhiteSpace(Host);

    [RelayCommand(CanExecute = nameof(CanSave))]
    public void Save()
    {
        var connection = new SavedConnection
        {
            Id = _id,
            Name = Name,
            Type = Type,
            Host = Host,
            Port = Port,
            Username = Username,
            Password = Password,
            AuthenticationType = AuthenticationType,
            KeyFilePath = KeyFilePath,
            Passphrase = Passphrase,
            GroupName = SelectedGroup,
            SerialPortName = SerialPortName,
            BaudRate = BaudRate,
            DataBits = DataBits,
            Parity = Parity,
            StopBits = StopBits,
            Handshake = Handshake
        };
        RequestClose?.Invoke(this, connection);
    }

    [RelayCommand]
    public void Cancel()
    {
        RequestClose?.Invoke(this, null);
    }
}
