FROM azurebatchrendering/centos-maya_2017u5:latest

ARG INSTALLER_SAS

COPY installArnold.sh /tmp/installArnold.sh
COPY mtoa.mod /usr/autodesk/mayaIO2017/modules/mtoa.mod

RUN sh /tmp/installArnold.sh $INSTALLER_SAS