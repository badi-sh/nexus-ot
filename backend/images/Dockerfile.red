FROM kalilinux/kali-rolling:latest

# Install Core, Networking & Red Team Tools
RUN apt-get update && apt-get install -y \
    python3 \
    python3-pip \
    iptables \
    iproute2 \
    iputils-ping \
    net-tools \
    nano \
    nmap \
    masscan \
    tcpdump \
    netcat-traditional \
    dnsutils \
    arp-scan \
    python3-scapy \
    curl \
    wget \
    && rm -rf /var/lib/apt/lists/*

# CRITICAL FIX: Switch to Legacy Iptables to work correctly inside Docker
RUN update-alternatives --set iptables /usr/sbin/iptables-legacy || true
RUN update-alternatives --set ip6tables /usr/sbin/ip6tables-legacy || true

WORKDIR /root

CMD ["tail", "-f", "/dev/null"]
