update config/filesystemproviders.config

<Provider alias="media" type="Obviuse.FileSystemProviders.AmazonS3Provider, Obviuse.FileSystemProviders">
    <Parameters>
      <add key="accessKeyId" value="** YOUR AWS ACCESS KEY HERE **" />
      <add key="secretAccessKey" value="** YOUR AWS SECRET ACCESS KEY HERE **" />
      <add key="bucketName" value="** YOUR BUCKET NAME HERE **" />
      <add key="region" value="eu-central-1" />
    </Parameters>
  </Provider>
</FileSystemProviders>

  
