#include <Arduino.h>
#include <BleCombo.h>

void setup() {
  Serial.begin(115200);
  Keyboard.begin();
  Mouse.begin();
  Serial.println("WandKey Firmware Started");
  Serial.println("Waiting for BLE connection...");
}

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

  String type = getValue(commandLine, ',', 0);
  
  if (type == "K") {
    String action = getValue(commandLine, ',', 1);
    
    if (action == "T") {
      int firstComma = commandLine.indexOf(',');
      int secondComma = commandLine.indexOf(',', firstComma + 1);
      if (secondComma != -1) {
        String text = commandLine.substring(secondComma + 1);
        for (int i = 0; i < text.length(); i++) {
          Keyboard.write(text[i]);
          delay(20);
        }
      }
    } else {
      String keyStr = getValue(commandLine, ',', 2);
      uint8_t key = 0;
      
      if (keyStr.length() == 1) {
        key = keyStr.charAt(0);
      } else {
        key = keyStr.toInt();
      }

      if (action == "D") {
        Keyboard.press(key);
      } else if (action == "U") {
        Keyboard.release(key);
      } else if (action == "P") {
        Keyboard.write(key);
      }
    }
  } else if (type == "M") {
    String action = getValue(commandLine, ',', 1);
    
    if (action == "MOVE") {
      int x = getValue(commandLine, ',', 2).toInt();
      int y = getValue(commandLine, ',', 3).toInt();
      Mouse.move(x, y);
    } else if (action == "CLICK") {
      String btnStr = getValue(commandLine, ',', 2);
      uint8_t btn = MOUSE_LEFT;
      if (btnStr == "RIGHT") btn = MOUSE_RIGHT;
      if (btnStr == "MIDDLE") btn = MOUSE_MIDDLE;
      
      Mouse.click(btn);
    }
  }
}

void loop() {
  if (Keyboard.isConnected()) {
    if (Serial.available() > 0) {
      String input = Serial.readStringUntil('\n');
      processCommand(input);
    }
  } else {
    static unsigned long lastLogTime = 0;
    if (millis() - lastLogTime > 3000) {
      Serial.println("Waiting for connection...");
      lastLogTime = millis();
    }
  }
}
