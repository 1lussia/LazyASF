package com.lazyasf.remote.net

import java.net.InetAddress
import java.net.Socket
import javax.net.SocketFactory

/**
 * Сокеты без алгоритма Нейгла. Иначе система копит крошечные сообщения «сдвинь курсор»
 * и отправляет их пачками раз в десятки миллисекунд — курсор на ТВ дёргается, «мало FPS».
 */
class NoDelaySocketFactory(private val delegate: SocketFactory) : SocketFactory() {
    private fun Socket.tuned() = apply { tcpNoDelay = true }

    override fun createSocket(): Socket = delegate.createSocket().tuned()
    override fun createSocket(host: String?, port: Int): Socket = delegate.createSocket(host, port).tuned()
    override fun createSocket(host: String?, port: Int, localHost: InetAddress?, localPort: Int): Socket =
        delegate.createSocket(host, port, localHost, localPort).tuned()
    override fun createSocket(host: InetAddress?, port: Int): Socket = delegate.createSocket(host, port).tuned()
    override fun createSocket(address: InetAddress?, port: Int, localAddress: InetAddress?, localPort: Int): Socket =
        delegate.createSocket(address, port, localAddress, localPort).tuned()
}
