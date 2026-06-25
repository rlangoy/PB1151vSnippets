# Import required modules
import time           # For delays and timing
from machine import Pin,UART
import sys
import json

''' Console JSON LED control + switch status reporting.

    Set an LED from the console (single line, then Enter):
        {"leds": {"id": "LED1", "value": 1}}     # LED1 on
        {"leds": {"id": "LED1", "value": 0}}     # LED1 off

    Multiple LEDs in one message (LEDs not listed are left unchanged):
        {"leds": [{"id": "LED1", "value": 1}, {"id": "LED4", "value": 0}]}

    Pressing a switch prints its status as JSON.
    SW1 fires on both edges, so a press then release prints:
        {"switches": [{"id": "SW1", "value": 1}]}   # pressed
        {"switches": [{"id": "SW1", "value": 0}]}   # released
'''

# Set up LEDs on GPIO 6, 7, 8, 9
leds = {
    "LED1" : Pin(6, Pin.OUT),
    "LED2" : Pin(7, Pin.OUT),
    "LED3" : Pin(8, Pin.OUT),
    "LED4" : Pin(9, Pin.OUT),
}

# Set up Buttons on GPIO 10, 11 ,12
sitches = {
   "SW1" : Pin(10, Pin.IN, Pin.PULL_DOWN),
   "SW2" : Pin(11, Pin.IN, Pin.PULL_DOWN),
   "SW3" : Pin(12, Pin.IN, Pin.PULL_DOWN),
}


def SW1Changed(SW):
    print('{"switches": [{"id": "SW1", "value": %d}]}' % SW.value())

def SW2Changed(SW):
    print('{"switches": [{"id": "SW2", "value": %d}]}' % SW.value())

def SW3Changed(SW):
    print('{"switches": [{"id": "SW3", "value": %d}]}' % SW.value())
       

#Setup button to activate IRQ handeling on input change
sitches["SW1"].irq(  trigger=Pin.IRQ_FALLING | Pin.IRQ_RISING ,
                handler=SW1Changed )

sitches["SW2"].irq(  trigger=Pin.IRQ_FALLING | Pin.IRQ_RISING ,
                handler=SW2Changed )

sitches["SW3"].irq(  trigger=Pin.IRQ_FALLING | Pin.IRQ_RISING ,
                handler=SW3Changed )


def applyJsonLedStates(payload):
    if(len(serialInput)==0):
       return
    
    try:
        payload=payload.lower()
        data = json.loads(payload)        
    except (ValueError, TypeError):
        print('{"ERROR" : "JSON not properly formated" }')
        print(payload)
        return

    #Return if key : "LEDS is missing" (This is not an error)
    led_list = data.get("leds") if isinstance(data, dict) else None
 
 
    # Accept both an object and an array:
    #   {"leds": {"id": "LED1", "value": 1}}         -> single object, wrap it
    #   {"leds": [{"id": "LED1", "value": 1}, ...]}  -> already a list 
    if isinstance(led_list, dict):
        led_list = [led_list]
 
    
    if not isinstance(led_list, list):        
        return

    for item in led_list:
        if not isinstance(item, dict):
            print('{"ERROR": "JSON "LEDS" array is not an object"}')
            continue
        
        led_id = item.get("id")
        value = item.get("value")
        pin = leds.get(led_id.upper()) if isinstance(led_id, str) else None
      
        if pin is None:
            print('{"ERROR": "JSON LEDS: unknown or missing LED id"}')   # this is the "bad ID" case
            continue
        if value not in (0, 1):
            print('{"ERROR": "JSON LEDS: value must be 0 or 1"}')          # this is the "bad value" case
            continue

        #No errors update the LED value
        pin.value(value)
            
# #Test JSON Message
# testPayload = '{"leds": [{"id": "LED1", "value": 1}, {"id": "LED4", "value": 0}]}'
# #Run the test
# applyJsonLedStates(testPayload)

while(True):
     #time.sleep(1)
     #Wait for Input serial Data
     serialInput = sys.stdin.readline().strip() 
     applyJsonLedStates(serialInput)
    
    

    
