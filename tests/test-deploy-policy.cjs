const {test}=require("node:test");
const assert=require("node:assert/strict");
const fs=require("node:fs");
const path=require("node:path");
const root=path.join(__dirname,"..");
const deploy=fs.readFileSync(path.join(root,".github/workflows/deploy-vps.yml"),"utf8");
const ci=fs.readFileSync(path.join(root,".github/workflows/ci.yml"),"utf8");
const start=deploy.indexOf("      - name: Require successful CI for exact commit");
const end=deploy.indexOf("      - name: Checkout",start);
const step=deploy.slice(start,end);
const script=step.split("          script: |\n")[1];
assert.ok(script);
const AsyncFunction=Object.getPrototypeOf(async function(){}).constructor;
const evaluate=new AsyncFunction("github","context","core","process",script);
const sha="a".repeat(40), other="b".repeat(40);
const good={head_sha:sha,head_branch:"main",event:"push",status:"completed",conclusion:"success",run_number:10,run_attempt:1};
async function check(runs,main=sha){
  const failures=[];
  const github={rest:{actions:{
    getWorkflow:async()=>({data:{id:42}}),
    listWorkflowRuns:async args=>{
      assert.equal(args.head_sha,sha);
      assert.equal(args.workflow_id,42);
      assert.equal(args.status,undefined);
      return {data:{workflow_runs:runs}};
    }
  },repos:{getBranch:async()=>({data:{commit:{sha:main}}})}}};
  await evaluate(github,{repo:{owner:"test",repo:"test"}},{setFailed:x=>failures.push(x)},{env:{TARGET_SHA:sha}});
  return failures;
}
test("exact main latest successful push only",async()=>{
  assert.deepEqual(await check([good]),[]);
  assert.ok((await check([good],other)).length);
  for(const patch of [{head_sha:other},{head_branch:"DEV-Khanh"},{event:"pull_request"},{status:"in_progress",conclusion:null},{conclusion:"failure"},{conclusion:"cancelled"}])
    assert.ok((await check([{...good,...patch}])).length);
  assert.ok((await check([])).length);
  for(const patch of [{status:"queued",conclusion:null},{conclusion:"failure"},{conclusion:"cancelled"}])
    assert.ok((await check([good,{...good,run_number:11,...patch}])).length);
  assert.ok((await check([good,{...good,run_attempt:2,conclusion:"failure"}])).length);
});
test("direct legacy merge path removed and required CI security gate",()=>{
  assert.equal(fs.existsSync(path.join(root,".github/workflows/auto-merge-dev-khanh.yml")),false);
  assert.match(ci,/permissions:\s*\n\s*contents: read/);
  assert.match(ci,/needs: \[unit-tests, database-tests, security-tests\]/);
  assert.ok(ci.includes('test "$SECURITY_TESTS_RESULT" = success'));
});
test("failed health cannot automatically restore production database",()=>{
  assert.ok(deploy.includes("Database schema is retained; automatic production database restore is intentionally disabled."));
  assert.ok(deploy.includes("cmp -s /opt/crabsense-api.previous/.schema-migration /opt/crabsense-api.failed/.schema-migration"));
  assert.ok(deploy.includes("Protocol-1 application rollback is unsafe"));
  assert.ok(deploy.includes("hardwareControlDisabled"));
});
