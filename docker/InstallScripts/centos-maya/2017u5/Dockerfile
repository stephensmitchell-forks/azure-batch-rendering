FROM azurebatchrendering/centos-maya:latest

ARG INSTALLER_SAS

COPY installMaya2017.sh /tmp/installMaya2017.sh
COPY maya2017.sh /usr/bin/maya2017.sh

RUN sh /tmp/installMaya2017.sh $INSTALLER_SAS 