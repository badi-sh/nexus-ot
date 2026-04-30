FROM ubuntu:22.04
ENV DEBIAN_FRONTEND=noninteractive

# Install core runtime dependencies
RUN apt-get update && apt-get install -y \
    curl gnupg2 iproute2 tcpdump python3 libpcap-dev \
    && rm -rf /var/lib/apt/lists/*

# Add the security:zeek repository and install Zeek
RUN echo 'deb http://download.opensuse.org/repositories/security:/zeek/xUbuntu_22.04/ /' | tee /etc/apt/sources.list.d/security:zeek.list && \
    curl -fsSL https://download.opensuse.org/repositories/security:zeek/xUbuntu_22.04/Release.key | gpg --dearmor | tee /etc/apt/trusted.gpg.d/security_zeek.gpg > /dev/null && \
    apt-get update && \
    apt-get install -y zeek-8.0

ENV PATH="/opt/zeek/bin:${PATH}"
RUN mkdir -p /opt/zeek/var/logs
WORKDIR /opt/zeek

CMD ["tail", "-f", "/dev/null"]
