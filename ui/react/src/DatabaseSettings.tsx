import {useEffect,useState} from "react";
import {Alert,AutoComplete,Button,Card,Checkbox,Form,Input,Select,Space,Switch,Typography} from "antd";
import {invoke} from "./bridge";
import type {FormInstance} from "antd";

interface BaseSqlSettings {
  server:string;
  windowsAuthentication:boolean;
  userName:string;
  password:string;
  rememberPassword:boolean;
  trustCertificate:boolean;
}
interface SavedSqlSettings extends Omit<BaseSqlSettings,"password"|"rememberPassword"> {
  configured:boolean;
  remembered:boolean;
  warning?:string|null;
}
interface SubiektValues extends BaseSqlSettings {
  database:string;
  drugGroup:string;
  warehouseId:number|null;
}
interface SavedSubiektSettings extends SavedSqlSettings {
  database:string;
  drugGroup:string;
  warehouseId:number|null;
}
interface CompanyDatabase { database:string;company:string; }
interface CompanyResult { companies:CompanyDatabase[];skipped:number; }
interface Warehouse {id:number;name:string;}
interface SubiektCheck {
  company:string;
  drugGroups:string[];
  warehouses:Warehouse[];
  groupValid:boolean;
  warehouseValid:boolean;
  message:string;
}
interface ArchiveCheck {saved:boolean;server:string;database:string;message:string;}
type BusyAction="load"|"discover"|"check"|"save"|null;

function ErrorNotice({message}:{message:string}) {
  return message?<Alert type="error" showIcon className="notice" title={message}/>:null;
}

function CredentialFields<T extends BaseSqlSettings>({form, showTrust = true}:{form:FormInstance<T>; showTrust?: boolean}) {
  const windows=Form.useWatch("windowsAuthentication",form)!==false;
  return <>
    <Form.Item label="Sposób logowania" name="windowsAuthentication"
      valuePropName="checked" extra="Włączone: konto Windows. Wyłączone: login i hasło SQL Server.">
      <Switch checkedChildren="Windows" unCheckedChildren="SQL"/>
    </Form.Item>
    <Space orientation="vertical" style={{width:"100%"}}>
      <Form.Item label="Login SQL Server" name="userName"
        rules={!windows?[{required:true,message:"Wpisz login SQL."}]:[]}>
        <Input autoComplete="username" disabled={windows}
          placeholder={windows?"Korzystasz z konta Windows":"Nazwa konta SQL"}/>
      </Form.Item>
      <Form.Item label="Hasło SQL Server" name="password"
        extra="Puste pole zachowa dotychczasowe zapamiętane hasło, jeśli serwer i login nie zmieniły się.">
        <Input.Password disabled={windows} autoComplete="new-password"
          placeholder="Nowe hasło lub pozostaw puste"/>
      </Form.Item>
    </Space>
    <Form.Item name="rememberPassword" valuePropName="checked">
      <Checkbox disabled={windows}>Zapamiętaj hasło na tym stanowisku (Windows DPAPI)</Checkbox>
    </Form.Item>
    {showTrust && <Form.Item name="trustCertificate" valuePropName="checked">
      <Checkbox>Zaufaj certyfikatowi tego serwera SQL</Checkbox>
    </Form.Item>}
  </>;
}

export function SubiektDatabaseSettings({onSaved}: {onSaved: () => void}){
  const [form]=Form.useForm<SubiektValues>();
  const [busy,setBusy]=useState<BusyAction>("load");
  const [error,setError]=useState("");
  const [notice,setNotice]=useState("");
  const [companies,setCompanies]=useState<CompanyDatabase[]>([]);
  const [groups,setGroups]=useState<string[]>([]);
  const [warehouses,setWarehouses]=useState<Warehouse[]>([]);
  const [checked,setChecked]=useState(false);

  useEffect(()=>{
    let active=true;
    void invoke<SavedSubiektSettings>("getSettings")
      .then(value=>{
        if(!active)return;
        form.setFieldsValue({
          ...value,password:"",rememberPassword:value.remembered,
          windowsAuthentication:value.windowsAuthentication??true,
          drugGroup:value.drugGroup||"Leki",warehouseId:value.warehouseId??null
        });
        if(value.warning)setNotice(value.warning);
      })
      .catch(e=>{if(active)setError(e instanceof Error?e.message:String(e));})
      .finally(()=>{if(active)setBusy(null);});
    return()=>{active=false;};
  },[form]);

  const fromForm=():Record<string,unknown>=>({...form.getFieldsValue(true)});

  async function discover(){
    if(busy)return;
    setBusy("discover");setError("");setNotice("");setChecked(false);
    try{
      const result=await invoke<CompanyResult>("discoverSubiektDatabases",fromForm());
      setCompanies(result.companies);
      if(result.companies.length===1&&!form.getFieldValue("database"))
        form.setFieldValue("database",result.companies[0].database);
      setNotice(result.companies.length
        ? "Znaleziono "+result.companies.length+" baz Subiekta."+
          (result.skipped?" Pominięto niedostępne bazy: "+result.skipped+".":"")
        : "Nie znaleziono dostępnych baz Subiekta. Możesz wpisać nazwę bazy ręcznie.");
    }catch(e){setError(e instanceof Error?e.message:String(e));}
    finally{setBusy(null);}
  }

  async function test(){
    if(busy)return;
    setBusy("check");setError("");setNotice("");setChecked(false);
    try{
      const result=await invoke<SubiektCheck>("testSubiektSettings",fromForm());
      setGroups(result.drugGroups);
      setWarehouses(result.warehouses);
      if(!result.groupValid&&result.drugGroups.length===1)
        form.setFieldValue("drugGroup",result.drugGroups[0]);
      if(!result.warehouseValid){
        form.setFieldValue("warehouseId",
          result.warehouses.length===1?result.warehouses[0].id:null);
      }
      setChecked(true);
      setNotice("Połączono z podmiotem: "+result.company+". Pobrano "+
        result.drugGroups.length+" grup towarów i "+result.warehouses.length+
        " magazynów. Sprawdź wybór grupy leków i magazynu, potem zapisz.");
    }catch(e){setError(e instanceof Error?e.message:String(e));}
    finally{setBusy(null);}
  }

  async function save(values:SubiektValues){
    if(busy)return;
    setBusy("save");setError("");setNotice("");
    try{
      await invoke("saveSettings",{...values});
      onSaved();
      setChecked(true);
      setNotice(values.windowsAuthentication || values.rememberPassword
        ? "Połączenie z Subiektem GT sprawdzone i zapisane na tym stanowisku."
        : "Połączenie z Subiektem GT sprawdzone. Hasło SQL działa do zamknięcia programu, " +
          "ale nie zostało zapamiętane. Aby dokumenty działały także po ponownym uruchomieniu, " +
          "zaznacz „Zapamiętaj hasło” i zapisz ustawienia jeszcze raz.");
    }catch(e){setError(e instanceof Error?e.message:String(e));}
    finally{setBusy(null);}
  }

  return <Card className="settings-card" loading={busy==="load"}>
    <Typography.Title level={4}>Połączenie z Subiektem GT</Typography.Title>
    <Typography.Paragraph type="secondary">
      Dokumenty, ceny, stany i magazyny są wyłącznie odczytywane z bazy Subiekta.
      Najpierw podaj serwer i dane logowania, pobierz bazy podmiotów, następnie
      sprawdź połączenie i wybierz magazyn.
    </Typography.Paragraph>
    <ErrorNotice message={error}/>
    {notice&&<Alert className="notice" showIcon type={checked?"success":"info"} title={notice}/>}
    <Form<SubiektValues> form={form} layout="vertical"
      initialValues={{windowsAuthentication:true,drugGroup:"Leki",warehouseId:null,rememberPassword:false}}
      onFinish={values=>{void save(values);}}
      onValuesChange={changed=>{
        setChecked(false);
        if(["server","database","windowsAuthentication","userName","password","trustCertificate"]
          .some(key=>Object.hasOwn(changed,key))){
          setGroups([]);setWarehouses([]);
          form.setFieldValue("warehouseId",null);
        }
      }}>
      <Form.Item label="Serwer Subiekta" name="server"
        rules={[{required:true,message:"Wpisz serwer SQL."}]}>
        <Input placeholder="np. SERWER\\INSERTGT lub SERWER,1433"/>
      </Form.Item>
      <CredentialFields form={form}/>
      <Form.Item label="Baza podmiotu Subiekta" name="database"
        rules={[{required:true,message:"Wybierz lub wpisz bazę podmiotu."}]}>
        <AutoComplete placeholder="Wybierz z listy lub wpisz nazwę bazy"
          filterOption={(query,option)=>String(option?.label??option?.value??"")
            .toLocaleLowerCase("pl").includes(query.toLocaleLowerCase("pl"))}
          options={companies.map(company=>({
            value:company.database,
            label:company.company?company.company+" — "+company.database:company.database
          }))}/>
      </Form.Item>
      <Space wrap style={{marginBottom:15}}>
        <Button onClick={()=>{void discover();}} loading={busy==="discover"} disabled={!!busy&&busy!=="discover"}>
          Pobierz bazy podmiotów
        </Button>
        <Button onClick={()=>{void test();}} loading={busy==="check"} disabled={!!busy&&busy!=="check"}>
          Sprawdź połączenie i pobierz magazyny
        </Button>
      </Space>
      <Form.Item label="Grupa leków w Subiekcie" name="drugGroup"
        rules={[{required:true,message:"Wybierz grupę leków."}]}>
        <AutoComplete placeholder="np. Leki" options={groups.map(group=>({value:group}))}
          filterOption={(query,option)=>String(option?.value??"")
            .toLocaleLowerCase("pl").includes(query.toLocaleLowerCase("pl"))}/>
      </Form.Item>
      <Form.Item label="Magazyn Subiekta" name="warehouseId"
        rules={[{required:true,message:"Wybierz magazyn z listy."}]}
        extra={warehouses.length?"Wybierz właściwy magazyn — stan i ceny zależą od tego wyboru."
          :"Użyj „Sprawdź połączenie i pobierz magazyny”, aby zobaczyć nazwy magazynów."}>
        <Select showSearch optionFilterProp="label"
          placeholder="Wybierz magazyn"
          options={warehouses.map(warehouse=>({
            label:warehouse.name+" (ID "+warehouse.id+")",value:warehouse.id
          }))}/>
      </Form.Item>
      <Button type="primary" htmlType="submit" loading={busy==="save"} disabled={!!busy&&busy!=="save"}>
        Sprawdź i zapisz Subiekt GT
      </Button>
    </Form>
  </Card>;
}

export function ProgramDatabaseSettings({administrator = false}: {administrator?: boolean}){
  const [form]=Form.useForm<BaseSqlSettings>();
  const [busy,setBusy]=useState<BusyAction>("load");
  const [error,setError]=useState("");
  const [notice,setNotice]=useState("");
  const [verified,setVerified]=useState(false);

  useEffect(()=>{
    let active=true;
    void invoke<SavedSqlSettings>(administrator?"getAdministratorSettings":"getArchiveSettings")
      .then(value=>{
        if(!active)return;
        form.setFieldsValue({...value,password:"",rememberPassword:value.remembered,
          windowsAuthentication:value.windowsAuthentication??true});
        if(value.warning)setNotice(value.warning);
      })
      .catch(e=>{if(active)setError(e instanceof Error?e.message:String(e));})
      .finally(()=>{if(active)setBusy(null);});
    return()=>{active=false;};
  },[form,administrator]);

  async function run(save: boolean, values?:BaseSqlSettings){
    if(busy)return;
    setBusy(save?"save":"check");
    setError("");setNotice("");setVerified(false);
    try{
      const action = administrator
        ? (save ? "saveAdministratorSettings" : "testAdministratorSettings")
        : (save ? "saveArchiveSettings" : "testArchiveSettings");
      const result=await invoke<ArchiveCheck>(action,{
        ...(values??form.getFieldsValue(true))
      });
      if (save && administrator) form.setFieldValue("password","");
      setVerified(true);setNotice(result.message);
    }catch(e){setError(e instanceof Error?e.message:String(e));}
    finally{setBusy(null);}
  }
  return <Card className="settings-card" loading={busy==="load"}>
    <Typography.Title level={4}>{administrator?"Konto administratora SQL":"Połączenie z bazą Karty Leczenia"}</Typography.Title>
    {administrator ? <Typography.Paragraph type="secondary">
      Konto z rolą sysadmin na serwerze bazy Karty Leczenia. Program używa go do
      trwałego usuwania kart z kosza i odtwarzania bazy po potwierdzeniu Windows Hello.
    </Typography.Paragraph> : <>
    <Typography.Paragraph type="secondary">
      Oddzielna baza programowa SQL Server zawierająca zapisane karty,
      katalog leków, ChPL i ustawienia lecznicy. Nie jest to baza Subiekta GT.
    </Typography.Paragraph>
    <Alert type="info" className="notice" showIcon
      title="Nazwa bazy SQL: KartyLeczenia"
      description="Ta techniczna nazwa pozostaje bez zmian podczas aktualizacji programu."/>
    </>}
    <ErrorNotice message={error}/>
    {notice&&<Alert className="notice" type={verified?"success":"info"} showIcon title={notice}/>}
    <Form<BaseSqlSettings> form={form} layout="vertical"
      initialValues={{windowsAuthentication:true,rememberPassword:false}}
      onFinish={values=>{void run(true,values);}}
      onValuesChange={()=>{setVerified(false);}}>
      <Form.Item label="Serwer bazy Karty Leczenia" name="server"
        rules={[{required:true,message:"Podaj adres serwera SQL."}]}>
        <Input readOnly={administrator} placeholder="np. SERWER,51433 lub SERWER\\KLARCHIVE"/>
      </Form.Item>
      <CredentialFields form={form} showTrust={!administrator}/>
      <Space wrap>
        <Button disabled={!!busy&&busy!=="check"} loading={busy==="check"}
          onClick={()=>{void run(false);}}>{administrator?"Sprawdź konto":"Sprawdź połączenie"}</Button>
        <Button type="primary" htmlType="submit"
          disabled={!!busy&&busy!=="save"} loading={busy==="save"}>
          {administrator?"Sprawdź i zapisz konto":"Sprawdź i zapisz bazę Karty Leczenia"}
        </Button>
      </Space>
    </Form>
  </Card>;
}
