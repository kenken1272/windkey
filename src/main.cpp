#include <Arduino.h>
#include <BleCombo.h>

void setup() {
  // Initialize Serial for debugging (USB CDC)
  Serial.begin(115200);
  
  // Initialize the BLE Composite HID Device
  // This advertises as a single device supporting both Keyboard and Mouse
  Keyboard.begin();
  Mouse.begin();
  
  Serial.println("WandKey Firmware Started");
  Serial.println("Waiting for BLE connection...");
}

// Helper function to parse comma-separated values
String getValue(String data, char separator, int index) {
  int found = 0;
  int strIndex[] = {0, -1};
  int maxIndex = data.length() - 1;

  for (int i = 0; i <= maxIndex && found <= index; i++) {
    if (data.charAt(i) == separator || i == maxIndex) {
      found++;
      strIndex[0] = strIndex[1] + 1;
      strIndex[1] = (i == maxIndex) ? i + 1 : i;
    }
  }
  return found > index ? data.substring(strIndex[0], strIndex[1]) : "";
}

void processCommand(String commandLine) {
  commandLine.trim();
  if (commandLine.length() == 0) return;

  // Protocol Format:
  // Keyboard: K,Action,Value
  //   Action: D (Down), U (Up), P (Press/Click), T (Type Text)
  //   Value: Char/Code for D/U/P, String for T
  // Mouse:    M,Action,Val1,Val2
  //   Action: MOVE (x,y), CLICK (LEFT/RIGHT)

  String type = getValue(commandLine, ',', 0);
  
  if (type == "K") {
    String action = getValue(commandLine, ',', 1);
    
    if (action == "T") { // Type Text: K,T,Hello World
      // Extract everything after the second comma
      int firstComma = commandLine.indexOf(',');
      int secondComma = commandLine.indexOf(',', firstComma + 1);
      if(secondComma != -1) {
        String text = commandLine.substring(secondComma + 1);
        for(int i=0; i<text.length(); i++) {
          Keyboard.write(text[i]);
          delay(20); 
        }
      }
    } else {
      // Single Key Command: K,D,a or K,D,131
      String keyStr = getValue(commandLine, ',', 2);
      uint8_t key = 0;
      
      // If length is 1, treat as char (e.g. "a"). If longer, treat as int code (e.g. "131")
      if (keyStr.length() == 1) {
        key = keyStr.charAt(0);
      } else {
        key = keyStr.toInt();
      }

      if (action == "D") {      // Key Down
        Keyboard.press(key);
      } else if (action == "U") { // Key Up
        Keyboard.release(key);
      } else if (action == "P") { // Key Press (Down+Up)
        Keyboard.write(key);
      }
    }
  } 
  else if (type == "M") {
    String action = getValue(commandLine, ',', 1);
    
    if (action == "MOVE") { // M,MOVE,x,y
      int x = getValue(commandLine, ',', 2).toInt();
      int y = getValue(commandLine, ',', 3).toInt();
      Mouse.move(x, y);
    } else if (action == "CLICK") { // M,CLICK,LEFT
      String btnStr = getValue(commandLine, ',', 2);
      uint8_t btn = MOUSE_LEFT;
      if (btnStr == "RIGHT") btn = MOUSE_RIGHT;
      if (btnStr == "MIDDLE") btn = MOUSE_MIDDLE;
      
      Mouse.click(btn);
    }
  }
}

void loop() {
  // Check if BLE is connected
  if (Keyboard.isConnected()) {
    
    // Check if data is available on USB Serial
    if (Serial.available() > 0) {
      // Read the incoming line
      String input = Serial.readStringUntil('\n');
      processCommand(input);
    }

  } else {
    // Not connected, print status every 3 seconds
    static unsigned long lastLogTime = 0;
    if (millis() - lastLogTime > 3000) {
      Serial.println("Waiting for connection...");
      lastLogTime = millis();
    }
  }
}