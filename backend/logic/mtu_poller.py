import time
import os
from pymodbus.client import ModbusTcpClient

# Docker DNS allows us to just use the container name!
# By default, we will look for a container named 'rtu' or whatever is passed.
TARGET_RTU = os.getenv("RTU_HOST", "rtu") 
POLL_RATE = int(os.getenv("POLL_RATE", "2")) # Poll every 2 seconds

def run_mtu_polling():
    print(f"[*] MTU Background Generator initialized.")
    print(f"[*] Target RTU Host: {TARGET_RTU}:502")
    
    client = ModbusTcpClient(TARGET_RTU, port=502)

    while True:
        try:
            if client.connect():
                # Read the first Coil (usually %QX0.0 in OpenPLC)
                result = client.read_coils(0, 1, slave=1)
                
                if not result.isError():
                    state = "CLOSED (OK)" if result.bits[0] else "TRIPPED (ALERT)"
                    print(f"[+] Heartbeat OK | Breaker State: {state}")
                else:
                    print("[-] Modbus Read Error: Device responded with an error.")
            else:
                print(f"[-] Connection failed. Is {TARGET_RTU} online and accessible?")
                
        except Exception as e:
            print(f"[!] Network Exception: {e}")

        # Wait before polling again to simulate normal SCADA intervals
        time.sleep(POLL_RATE)

if __name__ == "__main__":
    # Give the network a few seconds to spin up before hammering it
    time.sleep(5)
    run_mtu_polling()
