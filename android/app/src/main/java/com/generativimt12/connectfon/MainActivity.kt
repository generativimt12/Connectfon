package com.generativimt12.connectfon

import android.Manifest
import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothDevice
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat

class MainActivity : AppCompatActivity() {
    private lateinit var adapter: BluetoothAdapter
    private lateinit var devicesView: LinearLayout
    private lateinit var status: TextView
    private lateinit var details: TextView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)
        adapter = (getSystemService(BLUETOOTH_SERVICE) as android.bluetooth.BluetoothManager).adapter
        devicesView = findViewById(R.id.devices)
        status = findViewById(R.id.status)
        details = findViewById(R.id.details)
        findViewById<Button>(R.id.refresh).setOnClickListener { refresh() }
        requestBluetoothPermissions()
    }

    private fun requestBluetoothPermissions() {
        if (Build.VERSION.SDK_INT >= 31) {
            val needed = arrayOf(Manifest.permission.BLUETOOTH_CONNECT, Manifest.permission.BLUETOOTH_SCAN)
                .filter { ContextCompat.checkSelfPermission(this, it) != PackageManager.PERMISSION_GRANTED }
            if (needed.isNotEmpty()) ActivityCompat.requestPermissions(this, needed.toTypedArray(), 10)
            else refresh()
        } else refresh()
    }

    private fun refresh() {
        if (!adapter.isEnabled) {
            status.text = "Bluetooth: OFF"
            devicesView.removeAllViews()
            details.text = "Turn Bluetooth on, then press Refresh."
            return
        }
        status.text = "Bluetooth: ON"
        devicesView.removeAllViews()
        val paired = try { adapter.bondedDevices } catch (_: SecurityException) { emptySet() }
        if (paired.isEmpty()) {
            val t = TextView(this)
            t.text = "No paired devices."
            t.setTextColor(0xFFB9C0CC.toInt())
            t.setPadding(0, 18, 0, 18)
            devicesView.addView(t)
        } else {
            paired.sortedBy { it.name ?: it.address }.forEach { addDevice(it) }
        }
        details.text = "Adapter: ${adapter.name ?: "unknown"}\nPaired devices: ${paired.size}\nPublic Android Bluetooth API diagnostics enabled."
    }

    private fun addDevice(device: BluetoothDevice) {
        val button = Button(this)
        val name = try { device.name ?: "Unnamed device" } catch (_: SecurityException) { "Unnamed device" }
        val address = device.address
        button.text = "$name\n$address"
        button.setOnClickListener { showDeviceDiagnostics(device, name) }
        devicesView.addView(button)
    }

    private fun showDeviceDiagnostics(device: BluetoothDevice, name: String) {
        val uuids = try { device.uuids?.joinToString("\n") { it.uuid.toString() } ?: "UUIDs not exposed" }
            catch (_: SecurityException) { "Permission denied" }
        details.text = "Device: $name\nAddress: ${device.address}\nBond state: ${device.bondState}\nUUIDs:\n$uuids"
    }
}
