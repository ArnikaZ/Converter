# MineEyeConverter
MineEyeConverter is a Modbus protocol converter that bridges Modbus TCP clients with Modbus RTU/Serial slave devices. 
It enables communication between different protocol implementations and provides multiple operation modes to suit various requirements.
Features
- Multi-Protocol Support: Connect Modbus TCP clients to Modbus RTU/Serial devices
- Multiple Operation Modes:
	- Auto Mode: Direct 1:1 mapping between TCP and RTU registers
	- Manual Mode: Filtered access based on predefined register configuration
	- Learning Mode: Automatic discovery of available registers on slave devices
- Connection flexibility:
	- Serial (COM) connections with configurable parameters
	- RTU over TCP connections
- Security features:
	- IP address whitelist for client access control
	- Read/write permission management
- Windows Service Integration: Runs as a Windows service
- Detailed operation logs for diagnostics and monitoring

Configuration:
Configuration is managed through XML files:
- config.xml: the main configuration file that defines instances, connection parameters, and security settings.
- registers.xml: defines accessible registers in manual mode.


Setup:
- Configure the application by editing the config.xml file
- Configure predefined registers for manual mode by editing the registers.xml file
- Install as a Windows service: install -name:InstanceName
- Run service: run -name:InstanceName

Logging:
Logs are stored in the location configured in App.config. The default is: C:\Logs\RollingFileLog.txt

Operation modes:
- Auto Mode: provides direct 1:1 mapping between TCP and RTU registers. All data is transferred without filtering. This mode is suitable for trusted environments where all clients should have full access to all slave devices.
- Manual Mode: restricts access to only the registers explicitly defined in the registers.xml file. This provides an additional security layer to prevent unauthorized access to specific registers. This mode is recommended for production environments.
- Learning mode: automatically scans connected RTU devices to discover available registers and generates a configuration file for Manual mode. Use this mode during initial setup to create a baseline configuration.