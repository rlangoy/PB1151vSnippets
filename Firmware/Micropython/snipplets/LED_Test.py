# Import required modules
import time           # For delays and timing
from machine import Pin
import json

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
    leds["LED1"].value(SW.value())

def SW2Changed(SW):
    leds["LED2"].value(SW.value())

def SW3Changed(SW):
    leds["LED3"].value(SW.value())
       

#Setup button to activate IRQ handeling on input change
sitches["SW1"].irq(  trigger=Pin.IRQ_FALLING | Pin.IRQ_RISING ,
                handler=SW1Changed )

sitches["SW2"].irq(  trigger=Pin.IRQ_FALLING | Pin.IRQ_RISING ,
                handler=SW2Changed )

sitches["SW3"].irq(  trigger=Pin.IRQ_FALLING | Pin.IRQ_RISING ,
                handler=SW3Changed )

while(True):
     time.sleep(1)
    

    
